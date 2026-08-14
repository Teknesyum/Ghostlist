using System.Text;

namespace Ghostlist.Core;

public sealed record ShellLinkTarget(string? LocalPath, bool IsNetworkTarget);

public static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x01;
    private const uint HasLinkInfo = 0x02;
    private const uint VolumeIdAndLocalBasePath = 0x01;
    private const uint CommonNetworkRelativeLinkAndPathSuffix = 0x02;

    public static ShellLinkTarget? Read(byte[]? content)
    {
        if (content is null || content.Length < HeaderSize) return null;
        if (BitConverter.ToInt32(content, 0) != HeaderSize) return null;

        var flags = BitConverter.ToUInt32(content, 20);
        var offset = HeaderSize;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (offset + 2 > content.Length) return null;
            offset += 2 + BitConverter.ToUInt16(content, offset);
        }

        if ((flags & HasLinkInfo) == 0 || offset + 28 > content.Length) return null;

        var linkInfo = offset;
        var linkInfoSize = BitConverter.ToInt32(content, linkInfo);
        var linkInfoHeaderSize = BitConverter.ToInt32(content, linkInfo + 4);
        var linkInfoFlags = BitConverter.ToUInt32(content, linkInfo + 8);
        if (linkInfoSize <= 0 || linkInfo + linkInfoSize > content.Length) return null;

        if ((linkInfoFlags & VolumeIdAndLocalBasePath) == 0)
            return new ShellLinkTarget(null, (linkInfoFlags & CommonNetworkRelativeLinkAndPathSuffix) != 0);

        var unicode = linkInfoHeaderSize >= 0x24;
        var basePathOffset = BitConverter.ToInt32(content, linkInfo + (unicode ? 28 : 16));
        var suffixOffset = BitConverter.ToInt32(content, linkInfo + (unicode ? 32 : 24));
        var basePath = ReadString(content, linkInfo + basePathOffset, unicode);
        var suffix = ReadString(content, linkInfo + suffixOffset, unicode);
        if (basePath is null) return null;

        var full = string.IsNullOrEmpty(suffix) ? basePath : Path.Combine(basePath, suffix);
        return new ShellLinkTarget(full, full.StartsWith(@"\\", StringComparison.Ordinal));
    }

    private static string? ReadString(byte[] content, int start, bool unicode)
    {
        if (start < 0 || start >= content.Length) return null;
        if (!unicode)
        {
            var end = Array.IndexOf(content, (byte)0, start);
            if (end < 0) end = content.Length;
            return Encoding.Default.GetString(content, start, end - start);
        }
        var cursor = start;
        while (cursor + 1 < content.Length && (content[cursor] != 0 || content[cursor + 1] != 0)) cursor += 2;
        return Encoding.Unicode.GetString(content, start, cursor - start);
    }
}
