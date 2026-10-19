using System.Runtime.InteropServices;

namespace Kumo.Windows.Shell;

/// <summary>Moves files to the Recycle Bin, so removing an original is always recoverable.</summary>
public static partial class RecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    // Natural alignment matches the 64-bit SDK definition; Kumo ships for x64 and Arm64 only.
    [StructLayout(LayoutKind.Sequential)]
    private struct SHFILEOPSTRUCTW
    {
        public nint hwnd;
        public uint wFunc;
        public nint pFrom;
        public nint pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public nint hNameMappings;
        public nint lpszProgressTitle;
    }

    public static void Send(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The file to recycle no longer exists.", path);

        nint from = Marshal.StringToHGlobalUni(Path.GetFullPath(path) + "\0");
        try
        {
            var operation = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
            };
            int result = SHFileOperationW(ref operation);
            if (result != 0 || operation.fAnyOperationsAborted != 0)
                throw new IOException($"The file could not be moved to the Recycle Bin (code {result}).");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);
}
