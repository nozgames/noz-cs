using NoZ.Platform;

namespace NoZ;

public class NullAudioDriver : IAudioDriver
{
    public float MasterVolume { get; set; } = 1f;
    public float SoundVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 1f;

    public void Init() { }
    public void Shutdown() { }
    public nint CreateSound(ReadOnlySpan<byte> pcmData, int sampleRate, int channels, int bitsPerSample) => 1;
    public void DestroySound(nint handle) { }
    public ulong Play(nint sound, float volume, float pitch, bool loop) => 0;
    public void Stop(ulong handle) { }
    public bool IsPlaying(ulong handle) => false;
    public void SetVolume(ulong handle, float volume) { }
    public void SetPitch(ulong handle, float pitch) { }
    public float GetVolume(ulong handle) => 0;
    public float GetPitch(ulong handle) => 0;
    public void PlayMusic(nint sound) { }
    public void StopMusic() { }
    public bool IsMusicPlaying() => false;
}
