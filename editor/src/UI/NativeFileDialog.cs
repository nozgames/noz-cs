//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace NoZ.Editor;

internal static class NativeFileDialog
{
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int MAX_PATH = 260;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW ofn);

    public static string? ShowSaveFileDialog(nint ownerHwnd, string filter, string defaultExt, string? defaultFileName = null)
    {
        var fileBuffer = new char[MAX_PATH];
        if (defaultFileName != null)
            defaultFileName.AsSpan().CopyTo(fileBuffer);

        var handle = GCHandle.Alloc(fileBuffer, GCHandleType.Pinned);
        try
        {
            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = filter,
                lpstrFile = handle.AddrOfPinnedObject(),
                nMaxFile = MAX_PATH,
                lpstrDefExt = defaultExt,
                lpstrTitle = "Export to PNG",
                Flags = OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
            };

            if (!GetSaveFileNameW(ref ofn))
                return null;

            var span = fileBuffer.AsSpan();
            var nullIndex = span.IndexOf('\0');
            return nullIndex >= 0 ? new string(span[..nullIndex]) : new string(span);
        }
        finally
        {
            handle.Free();
        }
    }
}
