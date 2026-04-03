//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ.Editor;

public class SoundDocument : Document
{
    private SoundHandle _playHandle;

    // Preview sound built from in-memory samples + current modifiers
    private nint _previewHandle;
    private int _previewVersion = -1;
    private volatile bool _previewBuilding;
    private byte[]? _pendingPreviewPcm;
    private readonly object _previewLock = new();

    // Raw PCM samples as normalized floats (-1..1), loaded from WAV
    internal float[]? Samples { get; private set; }
    internal int SampleRate { get; private set; }
    internal int ChannelCount { get; private set; }
    internal int BitsPerSample { get; private set; }

    // Computed
    public float Duration => Samples != null && SampleRate > 0 && ChannelCount > 0
        ? (float)Samples.Length / ChannelCount / SampleRate
        : 0f;

    // Waveform display cache: downsampled min/max pairs
    internal float[]? WaveformMin { get; private set; }
    internal float[]? WaveformMax { get; private set; }
    internal int WaveformLength { get; private set; }

    // Non-destructive modifier properties (stored in .meta)
    public float VolumeMin { get; set; } = 1f;
    public float VolumeMax { get; set; } = 1f;
    public float PitchMin { get; set; } = 1f;
    public float PitchMax { get; set; } = 1f;
    public float FadeIn { get; set; } // 0-1, ratio of whole clip, relative to trim start
    public float FadeOut { get; set; } // 0-1, ratio of whole clip, relative to trim end
    public float TrimStart { get; set; }
    public float TrimEnd { get; set; }
    public float NormalizeTarget { get; set; }

    public static void RegisterDef()
    {
        DocumentDef<SoundDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sound,
            Name = "Sound",
            Extensions = [".wav"],
            Factory = () => new SoundDocument(),
            EditorFactory = doc => new SoundEditor((SoundDocument)doc),
            Icon = () => EditorAssets.Sprites.AssetIconSound,
        });
    }

    public override void Load()
    {
        LoadWavSamples();
        BuildWaveformCache();
    }

    public override void PostLoad() { }

    public override void Reload() { }

    public override void Dispose()
    {
        DestroyPreview();
        base.Dispose();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        VolumeMin = meta.GetFloat("sound", "volumeMin", 1f);
        VolumeMax = meta.GetFloat("sound", "volumeMax", 1f);
        PitchMin = meta.GetFloat("sound", "pitchMin", 1f);
        PitchMax = meta.GetFloat("sound", "pitchMax", 1f);
        FadeIn = meta.GetFloat("sound", "fadeIn", 0f);
        FadeOut = meta.GetFloat("sound", "fadeOut", 0f);
        TrimStart = meta.GetFloat("sound", "trimStart", 0f);
        TrimEnd = meta.GetFloat("sound", "trimEnd", 0f);
        NormalizeTarget = meta.GetFloat("sound", "normalize", 0f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (VolumeMin != 1f) meta.SetFloat("sound", "volumeMin", VolumeMin);
        if (VolumeMax != 1f) meta.SetFloat("sound", "volumeMax", VolumeMax);
        if (PitchMin != 1f) meta.SetFloat("sound", "pitchMin", PitchMin);
        if (PitchMax != 1f) meta.SetFloat("sound", "pitchMax", PitchMax);
        if (FadeIn > 0f) meta.SetFloat("sound", "fadeIn", FadeIn);
        if (FadeOut > 0f) meta.SetFloat("sound", "fadeOut", FadeOut);
        if (TrimStart > 0f) meta.SetFloat("sound", "trimStart", TrimStart);
        if (TrimEnd > 0f) meta.SetFloat("sound", "trimEnd", TrimEnd);
        if (NormalizeTarget > 0f) meta.SetFloat("sound", "normalize", NormalizeTarget);
    }

    public void ApplyChanges()
    {
        IncrementVersion();
        BuildWaveformCache();
        RebuildPreviewAsync();
    }

    private float[]? ProcessSamples()
    {
        return ProcessSamplesWithParams(
            Samples!, SampleRate, ChannelCount,
            TrimStart, TrimEnd, NormalizeTarget, FadeIn, FadeOut);
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        var processed = ProcessSamples();
        if (processed == null)
            return;

        var pcmData = ConvertToPcm(processed, BitsPerSample);

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sound, Sound.Version);
        writer.Write(SampleRate);
        writer.Write(ChannelCount);
        writer.Write(BitsPerSample);
        writer.Write(pcmData.Length);
        writer.Write(VolumeMin);
        writer.Write(VolumeMax);
        writer.Write(PitchMin);
        writer.Write(PitchMax);
        writer.Write(pcmData);
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconSound);
        }
    }

    public override bool CanPlay => Samples != null;
    public override bool IsPlaying => Audio.IsPlaying(_playHandle);

    public override void Play()
    {
        if (Samples == null)
            return;

        InstallPendingPreview();

        if (_previewVersion != Version && !_previewBuilding)
            BuildPreviewSync();

        if (_previewHandle == 0)
            return;

        var volume = MathEx.RandomRange(VolumeMin, VolumeMax);
        var pitch = MathEx.RandomRange(PitchMin, PitchMax);
        _playHandle = new SoundHandle(Audio.Driver.Play(_previewHandle, volume, pitch, false));
    }

    public float PlaybackPosition => Audio.GetPlaybackPosition(_playHandle);

    public override void Stop()
    {
        Audio.Stop(_playHandle);
    }

    private void RebuildPreviewAsync()
    {
        if (Samples == null)
            return;

        var capturedVersion = Version;

        var trimStart = TrimStart;
        var trimEnd = TrimEnd;
        var normalizeTarget = NormalizeTarget;
        var fadeIn = FadeIn;
        var fadeOut = FadeOut;
        var sampleRate = SampleRate;
        var channelCount = ChannelCount;
        var bitsPerSample = BitsPerSample;
        var samples = Samples;

        _previewBuilding = true;

        Task.Run(() =>
        {
            var processed = ProcessSamplesWithParams(
                samples, sampleRate, channelCount,
                trimStart, trimEnd, normalizeTarget, fadeIn, fadeOut);

            if (processed == null)
            {
                _previewBuilding = false;
                return;
            }

            var pcm = ConvertToPcm(processed, bitsPerSample);

            lock (_previewLock)
            {
                if (capturedVersion >= _previewVersion)
                    _pendingPreviewPcm = pcm;
            }

            _previewBuilding = false;
        });
    }

    private void InstallPendingPreview()
    {
        byte[]? pcm;
        lock (_previewLock)
        {
            pcm = _pendingPreviewPcm;
            _pendingPreviewPcm = null;
        }

        if (pcm == null)
            return;

        if (_previewHandle != 0)
            Audio.Driver.DestroySound(_previewHandle);

        _previewHandle = Audio.Driver.CreateSound(pcm, SampleRate, ChannelCount, BitsPerSample);
        _previewVersion = Version;
    }

    private void BuildPreviewSync()
    {
        var processed = ProcessSamples();
        if (processed == null)
            return;

        var pcm = ConvertToPcm(processed, BitsPerSample);

        if (_previewHandle != 0)
            Audio.Driver.DestroySound(_previewHandle);

        _previewHandle = Audio.Driver.CreateSound(pcm, SampleRate, ChannelCount, BitsPerSample);
        _previewVersion = Version;
    }

    private void DestroyPreview()
    {
        if (_previewHandle != 0)
        {
            Audio.Driver.DestroySound(_previewHandle);
            _previewHandle = 0;
        }
        _previewVersion = -1;
    }

    private static float[]? ProcessSamplesWithParams(
        float[] samples, int sampleRate, int channelCount,
        float trimStart, float trimEnd, float normalizeTarget,
        float fadeInDuration, float fadeOutDuration)
    {
        var trimStartSample = (int)(trimStart * samples.Length);
        var trimEndSample = trimEnd > 0f
            ? (int)(trimEnd * samples.Length)
            : samples.Length;

        trimStartSample = Math.Clamp(trimStartSample, 0, samples.Length);
        trimEndSample = Math.Clamp(trimEndSample, trimStartSample, samples.Length);

        var trimmedLength = trimEndSample - trimStartSample;
        if (trimmedLength <= 0)
            return null;

        var processed = new float[trimmedLength];
        Array.Copy(samples, trimStartSample, processed, 0, trimmedLength);

        if (normalizeTarget > 0f)
        {
            var peak = 0f;
            for (var i = 0; i < processed.Length; i++)
                peak = MathF.Max(peak, MathF.Abs(processed[i]));

            if (peak > 0f)
            {
                var scale = normalizeTarget / peak;
                for (var i = 0; i < processed.Length; i++)
                    processed[i] *= scale;
            }
        }

        if (fadeInDuration > 0f)
        {
            var fadeSamples = (int)(fadeInDuration * samples.Length);
            fadeSamples = Math.Min(fadeSamples, processed.Length);
            for (var i = 0; i < fadeSamples; i++)
                processed[i] *= (float)i / fadeSamples;
        }

        if (fadeOutDuration > 0f)
        {
            var fadeSamples = (int)(fadeOutDuration * samples.Length);
            fadeSamples = Math.Min(fadeSamples, processed.Length);
            for (var i = 0; i < fadeSamples; i++)
                processed[processed.Length - 1 - i] *= (float)i / fadeSamples;
        }

        return processed;
    }

    private void LoadWavSamples()
    {
        using var fileStream = File.OpenRead(Path);
        using var reader = new BinaryReader(fileStream);

        // RIFF header
        var riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff != "RIFF") return;
        reader.ReadUInt32(); // chunk size
        var wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wave != "WAVE") return;

        // fmt chunk
        var fmt = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (fmt != "fmt ") return;
        var fmtSize = reader.ReadUInt32();
        var audioFormat = reader.ReadUInt16();
        var numChannels = reader.ReadUInt16();
        var sampleRate = reader.ReadUInt32();
        reader.ReadUInt32(); // byte rate
        reader.ReadUInt16(); // block align
        var bitsPerSample = reader.ReadUInt16();

        if (audioFormat != 1) return;
        if (fmtSize > 16) reader.ReadBytes((int)(fmtSize - 16));

        // Find data chunk
        uint dataSize = 0;
        while (fileStream.Position < fileStream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadUInt32();
            if (chunkId == "data")
            {
                dataSize = chunkSize;
                break;
            }
            reader.ReadBytes((int)chunkSize);
        }

        if (dataSize == 0) return;

        SampleRate = (int)sampleRate;
        ChannelCount = numChannels;
        BitsPerSample = bitsPerSample;

        var pcmData = reader.ReadBytes((int)dataSize);
        Samples = ConvertToFloat(pcmData, bitsPerSample);
    }

    private static float[] ConvertToFloat(byte[] pcm, int bitsPerSample)
    {
        if (bitsPerSample == 16)
        {
            var count = pcm.Length / 2;
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = sample / 32768f;
            }
            return samples;
        }
        else // 8-bit
        {
            var samples = new float[pcm.Length];
            for (var i = 0; i < pcm.Length; i++)
                samples[i] = (pcm[i] - 128) / 128f;
            return samples;
        }
    }

    private static byte[] ConvertToPcm(float[] samples, int bitsPerSample)
    {
        if (bitsPerSample == 16)
        {
            var pcm = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var clamped = Math.Clamp(samples[i], -1f, 1f);
                var value = (short)(clamped * 32767f);
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }
            return pcm;
        }
        else // 8-bit
        {
            var pcm = new byte[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var clamped = Math.Clamp(samples[i], -1f, 1f);
                pcm[i] = (byte)(clamped * 128f + 128f);
            }
            return pcm;
        }
    }

    internal void BuildWaveformCache(int maxBuckets = 2048)
    {
        if (Samples == null || Samples.Length == 0)
        {
            WaveformMin = null;
            WaveformMax = null;
            WaveformLength = 0;
            return;
        }

        var normalizeScale = 1f;
        if (NormalizeTarget > 0f)
        {
            var peak = 0f;
            for (var i = 0; i < Samples.Length; i++)
                peak = MathF.Max(peak, MathF.Abs(Samples[i]));
            if (peak > 0f)
                normalizeScale = NormalizeTarget / peak;
        }

        var monoCount = Samples.Length / ChannelCount;
        var bucketCount = Math.Min(monoCount, maxBuckets);
        var samplesPerBucket = (float)monoCount / bucketCount;

        WaveformMin = new float[bucketCount];
        WaveformMax = new float[bucketCount];
        WaveformLength = bucketCount;

        for (var b = 0; b < bucketCount; b++)
        {
            var start = (int)(b * samplesPerBucket);
            var end = (int)((b + 1) * samplesPerBucket);
            end = Math.Min(end, monoCount);

            var min = float.MaxValue;
            var max = float.MinValue;

            for (var s = start; s < end; s++)
            {
                var value = 0f;
                for (var c = 0; c < ChannelCount; c++)
                    value += Samples[s * ChannelCount + c];
                value = value / ChannelCount * normalizeScale;

                if (value < min) min = value;
                if (value > max) max = value;
            }

            WaveformMin[b] = min;
            WaveformMax[b] = max;
        }
    }
}
