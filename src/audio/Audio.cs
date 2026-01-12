//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public static class Audio
{
    internal static void Init()
    {
        //PlatformInitAudio();
        
    }

    internal static void Shutdown()
    {
        //StopMusic();
        //PlatformShutdownAudio();
    }
    
    static void SetMasterVolume(float volume)
    {
//        PlatformSetMasterVolume(volume);
    }

    static float GetMasterVolume()
    {
        //return PlatformGetMasterVolume();
        return 0.0f;
    }

    static void SetSoundVolume(float volume)
    {
        
        //PlatformSetSoundVolume(volume);
    }

    static float GetSoundVolume()
    {
        return 0.0f;
        //return PlatformGetSoundVolume();
    }

    static void SetMusicVolume(float volume)
    {
        //PlatformSetMusicVolume(volume);
    }

    static float GetMusicVolume()
    {
        return 0.0f;
        //return PlatformGetMusicVolume();
    }

    static void PlayMusic(Sound sound)
    {
        if (IsMusicPlaying())
            StopMusic();

        //PlayMusicInternal(sound);
    }

    static void StopMusic()
    {
        if (!IsMusicPlaying())
            return;

//        PlatformStopMusic();
    }

    static bool IsMusicPlaying()
    {
        //return PlatformIsMusicPlaying();
        return false;
    }

    static void Stop(in SoundHandle handle) {
//        PlatformStopSound({
//            handle.value
//        });
    }
}
