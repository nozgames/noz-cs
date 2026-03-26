//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public class Sound : Asset
{
    internal const ushort Version = 1;

    internal nint PlatformHandle { get; private set; }
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }
    public int DataSize { get; private set; }

    private Sound(string name) : base(AssetType.Sound, name)
    {
    }

    public Sound() : base(AssetType.Sound) { }

    protected override void Load(BinaryReader reader)
    {
        SampleRate = reader.ReadInt32();
        Channels = reader.ReadInt32();
        BitsPerSample = reader.ReadInt32();
        DataSize = reader.ReadInt32();

        var pcmData = reader.ReadBytes(DataSize);
        PlatformHandle = Audio.Driver.CreateSound(pcmData, SampleRate, Channels, BitsPerSample);
    }

    private static Asset Load(Stream stream, string name)
    {
        var sound = new Sound(name);
        using var reader = new BinaryReader(stream);
        sound.Load(reader);
        return sound;
    }

    public override void Dispose()
    {
        if (PlatformHandle != 0)
        {
            Audio.Driver.DestroySound(PlatformHandle);
            PlatformHandle = 0;
        }
        base.Dispose();
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sound, "Sound", typeof(Sound), Load, Version));
    }
}