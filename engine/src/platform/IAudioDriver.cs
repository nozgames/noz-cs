//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Platform;

public interface IAudioDriver
{
    void Init();
    void Shutdown();
    nint CreateSound(ReadOnlySpan<byte> pcmData, int sampleRate, int channels, int bitsPerSample);
    void DestroySound(nint handle);
    ulong Play(nint sound, float volume, float pitch, bool loop);
    void Stop(ulong handle);
    bool IsPlaying(ulong handle);
    void SetVolume(ulong handle, float volume);
    void SetPitch(ulong handle, float pitch);
    float GetVolume(ulong handle);
    float GetPitch(ulong handle);
    void PlayMusic(nint sound);
    void StopMusic();
    bool IsMusicPlaying();
    float MasterVolume { get; set; }
    float SoundVolume { get; set; }
    float MusicVolume { get; set; }
}
