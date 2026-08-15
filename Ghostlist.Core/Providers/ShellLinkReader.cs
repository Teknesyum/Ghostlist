using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Ghostlist.Core;

public sealed record ShellLinkTarget(string? LocalPath, bool IsNetworkTarget, bool IsAmbiguous = false);

public static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x01;
    private const uint HasLinkInfo = 0x02;
    private const uint VolumeIdAndLocalBasePath = 0x01;
    private const uint CommonNetworkRelativeLinkAndPathSuffix = 0x02;
    private const int UnicodeLinkInfoHeaderSize = 0x24;
    private const char ReplacementChar = (char)0xFFFD;
    private const char C1RangeStart = (char)0x80;
    private const char C1RangeEnd = (char)0x9F;

    private static readonly Encoding? AnsiEncoding = ResolveAnsiEncoding();

    public static Encoding? SystemAnsiEncoding => AnsiEncoding;

    public static ShellLinkTarget? Read(byte[]? content) => Read(content, null);

    public static ShellLinkTarget? Read(byte[]? content, Encoding? ansiEncoding)
    {
        var ansi = ansiEncoding ?? AnsiEncoding;
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

        var hasUnicodeFields = linkInfoHeaderSize >= UnicodeLinkInfoHeaderSize && linkInfo + 36 <= content.Length;
        var unicodeBaseOffset = hasUnicodeFields ? BitConverter.ToInt32(content, linkInfo + 28) : 0;
        var unicodeSuffixOffset = hasUnicodeFields ? BitConverter.ToInt32(content, linkInfo + 32) : 0;

        string? basePath;
        string? suffix;
        var ambiguous = false;

        if (unicodeBaseOffset != 0)
        {
            basePath = ReadUnicodeString(content, linkInfo + unicodeBaseOffset);
            suffix = unicodeSuffixOffset != 0 ? ReadUnicodeString(content, linkInfo + unicodeSuffixOffset) : "";
        }
        else
        {
            var ansiBaseOffset = BitConverter.ToInt32(content, linkInfo + 16);
            var ansiSuffixOffset = BitConverter.ToInt32(content, linkInfo + 24);
            basePath = ReadAnsiString(content, linkInfo + ansiBaseOffset, ansi, out var baseAmbiguous);
            ambiguous = baseAmbiguous;
            if (ansiSuffixOffset != 0)
            {
                suffix = ReadAnsiString(content, linkInfo + ansiSuffixOffset, ansi, out var suffixAmbiguous);
                ambiguous = ambiguous || suffixAmbiguous;
            }
            else
            {
                suffix = "";
            }
        }

        if (basePath is null) return ambiguous ? new ShellLinkTarget(null, false, true) : null;
        if (ambiguous) return new ShellLinkTarget(null, false, true);

        var full = string.IsNullOrEmpty(suffix) ? basePath : Path.Combine(basePath, suffix);
        return new ShellLinkTarget(full, full.StartsWith(@"\\", StringComparison.Ordinal));
    }

    private static string? ReadAnsiString(byte[] content, int start, Encoding? encoding, out bool ambiguous)
    {
        ambiguous = false;
        if (start < 0 || start >= content.Length) return null;
        var end = Array.IndexOf(content, (byte)0, start);
        if (end < 0) end = content.Length;
        if (encoding is null)
        {
            ambiguous = true;
            return null;
        }
        string decoded;
        try
        {
            decoded = encoding.GetString(content, start, end - start);
        }
        catch (DecoderFallbackException)
        {
            ambiguous = true;
            return null;
        }
        if (HasUndefinedMapping(decoded))
        {
            ambiguous = true;
            return null;
        }
        return decoded;
    }

    private static bool HasUndefinedMapping(string value)
    {
        foreach (var ch in value)
        {
            if (ch == ReplacementChar) return true;
            if (ch >= C1RangeStart && ch <= C1RangeEnd) return true;
        }
        return false;
    }

    private static string ReadUnicodeString(byte[] content, int start)
    {
        var cursor = start;
        while (cursor + 1 < content.Length && (content[cursor] != 0 || content[cursor + 1] != 0)) cursor += 2;
        return Encoding.Unicode.GetString(content, start, cursor - start);
    }

    private static Encoding? ResolveAnsiEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
        }

        var codePage = GetSystemAnsiCodePage();
        try
        {
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch
        {
            try
            {
                return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            catch
            {
                return null;
            }
        }
    }

    private static int GetSystemAnsiCodePage()
    {
        try
        {
            var codePage = GetACP();
            if (codePage > 0) return codePage;
        }
        catch
        {
        }
        return CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
    }

    [DllImport("kernel32.dll")]
    private static extern int GetACP();
}
