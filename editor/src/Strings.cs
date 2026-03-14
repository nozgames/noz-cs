//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

[Localizable]
public static partial class Strings
{
    private static readonly string[] _numbers = InitNumbers();

    private static string[] InitNumbers()
    {
        var numbers = new string[1000];
        for (int i = 0; i < 1000; i++)
            numbers[i] = i.ToString();
        return numbers;
    }

    public static string Number(int value) =>
        (uint)value < 1000 ? _numbers[value] : value.ToString();

    public static void ColorHex(Color32 color, Span<char> hex)
    {
        if (hex.Length != 6) return;

        hex[0] = HexChar(color.R >> 4);
        hex[1] = HexChar(color.R & 0xF);
        hex[2] = HexChar(color.G >> 4);
        hex[3] = HexChar(color.G & 0xF);
        hex[4] = HexChar(color.B >> 4);
        hex[5] = HexChar(color.B & 0xF);
    }

    private static char HexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
}
