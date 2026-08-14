using System.Runtime.InteropServices;

namespace Ghostlist.Core;

public static class SystemRestore
{
    private const int BeginSystemChange = 100;
    private const int ModifySettings = 12;
    private const int MaxDescription = 64;

    public static bool TryCreate(string description)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var info = new RestorePointInfo
            {
                dwEventType = BeginSystemChange,
                dwRestorePtType = ModifySettings,
                llSequenceNumber = 0,
                szDescription = Shorten(description)
            };
            return SRSetRestorePointW(ref info, out var status) && status.nStatus == 0;
        }
        catch { return false; }
    }

    private static string Shorten(string description)
    {
        var text = string.IsNullOrWhiteSpace(description) ? "Ghostlist" : description.Trim();
        return text.Length <= MaxDescription ? text : text[..MaxDescription];
    }

    [DllImport("srclient.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SRSetRestorePointW(ref RestorePointInfo info, out StateManagerStatus status);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestorePointInfo
    {
        public int dwEventType;
        public int dwRestorePtType;
        public long llSequenceNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxDescription + 192)]
        public string szDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StateManagerStatus
    {
        public int nStatus;
        public long llSequenceNumber;
    }
}
