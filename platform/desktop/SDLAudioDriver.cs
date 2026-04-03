//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Threading;
using SDL;
using static SDL.SDL3;

namespace NoZ.Platform;

public unsafe class SDLAudioDriver : IAudioDriver
{
    private const int MaxSources = 32;
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BufferSamples = 1024;

    private SDL_AudioDeviceID _device;
    private SDL_AudioStream* _stream;

    private readonly AudioSource[] _sources = new AudioSource[MaxSources];
    private readonly List<SoundData> _sounds = [];
    private readonly object _soundsLock = new();

    private SoundData? _musicSound;
    private float _musicPosition;
    private volatile bool _musicPlaying;

    private float _masterVolume = 1f;
    private float _soundVolume = 1f;
    private float _musicVolume = 1f;

    private readonly float[] _mixBuffer = new float[BufferSamples * Channels];

    private static SDLAudioDriver? _instance;

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0f, 1f);
    }

    public float SoundVolume
    {
        get => _soundVolume;
        set => _soundVolume = Math.Clamp(value, 0f, 1f);
    }

    public float MusicVolume
    {
        get => _musicVolume;
        set => _musicVolume = Math.Clamp(value, 0f, 1f);
    }

    public void Init()
    {
        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            throw new Exception($"Failed to init SDL audio: {SDL_GetError()}");

        var spec = new SDL_AudioSpec
        {
            freq = SampleRate,
            format = SDL_AudioFormat.SDL_AUDIO_F32LE,
            channels = Channels
        };

        _instance = this;
        _stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &AudioCallback, nint.Zero);
        if (_stream == null)
            throw new Exception($"Failed to open audio device: {SDL_GetError()}");

        _device = SDL_GetAudioStreamDevice(_stream);
        SDL_ResumeAudioStreamDevice(_stream);

        for (var i = 0; i < MaxSources; i++)
            _sources[i] = new AudioSource();
    }

    public void Shutdown()
    {
        _instance = null;

        if (_stream != null)
        {
            SDL_DestroyAudioStream(_stream);
            _stream = null;
        }

        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);

        foreach (var sound in _sounds)
            sound.Samples = null;
        _sounds.Clear();
    }

    public nint CreateSound(ReadOnlySpan<byte> pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        // Pre-convert PCM to float samples at load time
        var bytesPerSample = bitsPerSample / 8;
        var sampleCount = pcmData.Length / bytesPerSample;
        var samples = new float[sampleCount];

        if (bytesPerSample == 2)
        {
            for (var i = 0; i < sampleCount; i++)
            {
                var offset = i * 2;
                var s16 = (short)(pcmData[offset] | (pcmData[offset + 1] << 8));
                samples[i] = s16 / 32768f;
            }
        }

        var soundData = new SoundData { Samples = samples };

        lock (_soundsLock)
        {
            _sounds.Add(soundData);
            return _sounds.Count; // 1-indexed handle
        }
    }

    public void DestroySound(nint handle)
    {
        lock (_soundsLock)
        {
            var index = (int)handle - 1;
            if (index >= 0 && index < _sounds.Count)
                _sounds[index].Samples = null;
        }
    }

    public ulong Play(nint sound, float volume, float pitch, bool loop)
    {
        var soundIndex = (int)sound - 1;
        if (soundIndex < 0 || soundIndex >= _sounds.Count || _sounds[soundIndex].Samples == null)
            return 0;

        for (var i = 0; i < MaxSources; i++)
        {
            if (!Volatile.Read(ref _sources[i].Playing))
            {
                _sources[i].SoundIndex = soundIndex;
                _sources[i].Position = 0f;
                _sources[i].Volume = volume;
                _sources[i].Pitch = pitch;
                _sources[i].Loop = loop;
                _sources[i].Generation++;
                Volatile.Write(ref _sources[i].Playing, true);

                return ((ulong)_sources[i].Generation << 32) | (uint)i;
            }
        }

        return 0;
    }

    public void Stop(ulong handle)
    {
        var (index, generation) = DecodeHandle(handle);
        if (index < MaxSources && _sources[index].Generation == generation)
            Volatile.Write(ref _sources[index].Playing, false);
    }

    public bool IsPlaying(ulong handle)
    {
        var (index, generation) = DecodeHandle(handle);
        return index < MaxSources && _sources[index].Generation == generation && Volatile.Read(ref _sources[index].Playing);
    }

    public void SetVolume(ulong handle, float volume)
    {
        var (index, generation) = DecodeHandle(handle);
        if (index < MaxSources && _sources[index].Generation == generation)
            _sources[index].Volume = Math.Clamp(volume, 0f, 1f);
    }

    public float GetPlaybackPosition(ulong handle)
    {
        var (index, generation) = DecodeHandle(handle);
        if (index >= MaxSources || _sources[index].Generation != generation || !Volatile.Read(ref _sources[index].Playing))
            return 0f;

        var sound = _sounds[_sources[index].SoundIndex];
        if (sound.Samples == null || sound.Samples.Length == 0)
            return 0f;

        return Math.Clamp(_sources[index].Position / sound.Samples.Length, 0f, 1f);
    }

    public void SetPitch(ulong handle, float pitch)
    {
        var (index, generation) = DecodeHandle(handle);
        if (index < MaxSources && _sources[index].Generation == generation)
            _sources[index].Pitch = Math.Clamp(pitch, 0.5f, 2f);
    }

    public float GetVolume(ulong handle)
    {
        var (index, generation) = DecodeHandle(handle);
        return index < MaxSources && _sources[index].Generation == generation
            ? _sources[index].Volume
            : 0f;
    }

    public float GetPitch(ulong handle)
    {
        var (index, generation) = DecodeHandle(handle);
        return index < MaxSources && _sources[index].Generation == generation
            ? _sources[index].Pitch
            : 1f;
    }

    public void PlayMusic(nint sound)
    {
        var soundIndex = (int)sound - 1;
        if (soundIndex < 0 || soundIndex >= _sounds.Count)
            return;

        _musicSound = _sounds[soundIndex];
        _musicPosition = 0f;
        _musicPlaying = true;
    }

    public void StopMusic()
    {
        _musicPlaying = false;
        _musicSound = null;
    }

    public bool IsMusicPlaying() => _musicPlaying;

    private static (uint index, uint generation) DecodeHandle(ulong handle)
    {
        return ((uint)(handle & 0xFFFFFFFF), (uint)(handle >> 32));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static void AudioCallback(nint userdata, SDL_AudioStream* stream, int additionalAmount, int totalAmount)
    {
        if (_instance == null) return;
        _instance.MixAudio(stream, additionalAmount);
    }

    private void MixAudio(SDL_AudioStream* stream, int bytesNeeded)
    {
        var samplesNeeded = bytesNeeded / sizeof(float);
        if (samplesNeeded > _mixBuffer.Length)
            samplesNeeded = _mixBuffer.Length;

        Array.Clear(_mixBuffer, 0, samplesNeeded);

        var framesNeeded = samplesNeeded / Channels;
        var masterVol = _masterVolume;
        var soundVol = _soundVolume * masterVol;
        var musicVol = _musicVolume * masterVol;

        // Mix music
        if (_musicPlaying && _musicSound?.Samples != null)
            MixSound(_musicSound, ref _musicPosition, musicVol, 1f, true, framesNeeded);

        // Mix sound effects
        for (var i = 0; i < MaxSources; i++)
        {
            ref var source = ref _sources[i];
            if (!Volatile.Read(ref source.Playing)) continue;

            var soundData = _sounds[source.SoundIndex];
            if (soundData.Samples == null)
            {
                Volatile.Write(ref source.Playing, false);
                continue;
            }

            var vol = source.Volume * soundVol;
            if (MixSound(soundData, ref source.Position, vol, source.Pitch, source.Loop, framesNeeded))
                Volatile.Write(ref source.Playing, false);
        }

        // Clip output to prevent distortion
        for (var i = 0; i < samplesNeeded; i++)
            _mixBuffer[i] = Math.Clamp(_mixBuffer[i], -1f, 1f);

        fixed (float* buffer = _mixBuffer)
        {
            SDL_PutAudioStreamData(stream, (nint)buffer, samplesNeeded * sizeof(float));
        }
    }

    private bool MixSound(SoundData sound, ref float position, float volume, float pitch, bool loop, int framesToMix)
    {
        var samples = sound.Samples;
        if (samples == null) return true;

        var totalSamples = samples.Length;

        for (var frame = 0; frame < framesToMix; frame++)
        {
            var srcFrame = (int)position;
            if (srcFrame >= totalSamples)
            {
                if (loop) { position = 0f; srcFrame = 0; }
                else return true;
            }

            var sample = samples[srcFrame] * volume;
            var outIndex = frame * Channels;
            _mixBuffer[outIndex] += sample;
            _mixBuffer[outIndex + 1] += sample;

            position += pitch;
        }

        return false;
    }

    private struct AudioSource
    {
        public int SoundIndex;
        public float Position;
        public float Volume;
        public float Pitch;
        public bool Loop;
        public bool Playing;
        public uint Generation;
    }

    private class SoundData
    {
        public float[]? Samples;
    }
}
