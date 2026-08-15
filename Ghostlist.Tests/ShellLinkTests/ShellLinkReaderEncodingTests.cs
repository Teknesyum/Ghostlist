using System.Text;
using Ghostlist.Core;

namespace Ghostlist.Tests.ShellLinkTests;

public class ShellLinkReaderEncodingTests
{
    [Fact]
    public void AnsiTurkishTargetIsDecodedWithTheSystemCodePageInsteadOfUtf8()
    {
        var basePathBytes = new byte[] { 0x43, 0x3A, 0x5C, 0x4B, 0xFD, 0x73, 0x61, 0x79, 0x6F, 0x6C, 0x5C };
        var suffixBytes = new byte[] { 0x68, 0x77, 0x69, 0x2E, 0x65, 0x78, 0x65 };

        var link = ShellLinkReader.Read(BuildAnsiOnly(basePathBytes, suffixBytes));

        Assert.NotNull(link);
        Assert.False(link.IsAmbiguous);
        Assert.Equal(@"C:\Kısayol\hwi.exe", link.LocalPath);
    }

    [Fact]
    public void UnicodeLinkInfoFieldsAreUsedInsteadOfTheAnsiOnes()
    {
        var basePathUnicode = Encoding.Unicode.GetBytes(@"C:\Görsel\");
        var suffixUnicode = Encoding.Unicode.GetBytes("pencere.exe");

        var link = ShellLinkReader.Read(BuildWithUnicodeFields(
            ansiBasePath: new byte[] { 0x58 },
            ansiSuffix: new byte[] { 0x59 },
            unicodeBasePath: basePathUnicode,
            unicodeSuffix: suffixUnicode));

        Assert.NotNull(link);
        Assert.False(link.IsAmbiguous);
        Assert.Equal(@"C:\Görsel\pencere.exe", link.LocalPath);
    }

    [Fact]
    public void TruncatedLinkInfoProducesNoTarget()
    {
        var header = BuildHeader(0x02);
        var truncated = header.Concat(new byte[10]).ToArray();

        Assert.Null(ShellLinkReader.Read(truncated));
    }

    [Fact]
    public void UndecodableAnsiBytesAreAmbiguousInsteadOfMisread()
    {
        var basePathBytes = new byte[] { 0x81 };
        var suffixBytes = new byte[] { 0x61, 0x70, 0x70, 0x2E, 0x65, 0x78, 0x65 };

        var link = ShellLinkReader.Read(BuildAnsiOnly(basePathBytes, suffixBytes));

        Assert.NotNull(link);
        Assert.True(link.IsAmbiguous);
        Assert.Null(link.LocalPath);
    }

    private static byte[] BuildHeader(uint linkFlags)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x4C);
        writer.Write(new byte[16]);
        writer.Write(linkFlags);
        writer.Write(new byte[0x4C - 24]);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildAnsiOnly(byte[] basePathBytes, byte[] suffixBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(BuildHeader(0x02));

        using var writer = new BinaryWriter(stream);
        const int headerSize = 0x1C;
        var basePathOffset = headerSize;
        var suffixOffset = basePathOffset + basePathBytes.Length + 1;
        var totalSize = suffixOffset + suffixBytes.Length + 1;

        writer.Write(totalSize);
        writer.Write(headerSize);
        writer.Write(0x01u);
        writer.Write(0);
        writer.Write(basePathOffset);
        writer.Write(0);
        writer.Write(suffixOffset);
        writer.Write(basePathBytes);
        writer.Write((byte)0);
        writer.Write(suffixBytes);
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildWithUnicodeFields(byte[] ansiBasePath, byte[] ansiSuffix, byte[] unicodeBasePath, byte[] unicodeSuffix)
    {
        using var stream = new MemoryStream();
        stream.Write(BuildHeader(0x82));

        using var writer = new BinaryWriter(stream);
        const int headerSize = 0x24;
        var ansiBaseOffset = headerSize;
        var ansiSuffixOffset = ansiBaseOffset + ansiBasePath.Length + 1;
        var unicodeBaseOffset = ansiSuffixOffset + ansiSuffix.Length + 1;
        var unicodeSuffixOffset = unicodeBaseOffset + unicodeBasePath.Length + 2;
        var totalSize = unicodeSuffixOffset + unicodeSuffix.Length + 2;

        writer.Write(totalSize);
        writer.Write(headerSize);
        writer.Write(0x01u);
        writer.Write(0);
        writer.Write(ansiBaseOffset);
        writer.Write(0);
        writer.Write(ansiSuffixOffset);
        writer.Write(unicodeBaseOffset);
        writer.Write(unicodeSuffixOffset);
        writer.Write(ansiBasePath);
        writer.Write((byte)0);
        writer.Write(ansiSuffix);
        writer.Write((byte)0);
        writer.Write(unicodeBasePath);
        writer.Write(new byte[2]);
        writer.Write(unicodeSuffix);
        writer.Write(new byte[2]);
        writer.Flush();
        return stream.ToArray();
    }
}
