using System.Buffers.Binary;
using System.Text;

namespace StormOS.Windows.Startup;

/// <summary>A resolved shell link.</summary>
/// <param name="TargetPath">Link target path.</param>
/// <param name="Arguments">Command line arguments.</param>
public sealed record ShellLinkInfo(string? TargetPath, string? Arguments);

/// <summary>
/// Minimal read-only parser for .lnk files following [MS-SHLLINK]. Reads the LinkInfo local base path and
/// the argument string without instantiating COM shell objects.
/// </summary>
public static class ShellLinkReader
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint HasName = 0x4;
    private const uint HasRelativePath = 0x8;
    private const uint HasWorkingDir = 0x10;
    private const uint HasArguments = 0x20;
    private const uint IsUnicode = 0x80;

    /// <summary>Reads a .lnk file.</summary>
    /// <param name="path">File path.</param>
    /// <returns>The link information, or <see langword="null"/> when the file is not a valid shell link.</returns>
    public static ShellLinkInfo? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length is > HeaderSize and < 1024 * 1024 ? Parse(File.ReadAllBytes(path)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses shell link bytes.</summary>
    /// <param name="data">File content.</param>
    /// <returns>The link information, or <see langword="null"/> when invalid.</returns>
    public static ShellLinkInfo? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize)
        {
            return null;
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]);
        var offset = HeaderSize;
        try
        {
            if ((flags & HasLinkTargetIdList) != 0)
            {
                offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            }

            string? target = null;
            if ((flags & HasLinkInfo) != 0)
            {
                var linkInfo = data[offset..];
                var linkInfoSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(linkInfo);
                target = ReadLocalBasePath(linkInfo[..linkInfoSize]);
                offset += linkInfoSize;
            }

            var unicode = (flags & IsUnicode) != 0;
            string? arguments = null;
            foreach (var (flag, isArguments) in new[] { (HasName, false), (HasRelativePath, false), (HasWorkingDir, false), (HasArguments, true) })
            {
                if ((flags & flag) == 0)
                {
                    continue;
                }

                var chars = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                offset += 2;
                var byteLength = unicode ? chars * 2 : chars;
                var value = unicode ? Encoding.Unicode.GetString(data.Slice(offset, byteLength)) : Encoding.Latin1.GetString(data.Slice(offset, byteLength));
                offset += byteLength;
                if (isArguments)
                {
                    arguments = value;
                }
            }

            return new ShellLinkInfo(target, arguments);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ReadLocalBasePath(ReadOnlySpan<byte> linkInfo)
    {
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[4..]);
        var linkInfoFlags = BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[8..]);
        if ((linkInfoFlags & 0x1) == 0)
        {
            return null;
        }

        if (headerSize >= 0x24)
        {
            var unicodeOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[0x1C..]);
            var unicodeSuffix = (int)BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[0x20..]);
            if (unicodeOffset > 0)
            {
                return ReadUnicodeZ(linkInfo[unicodeOffset..]) + (unicodeSuffix > 0 ? ReadUnicodeZ(linkInfo[unicodeSuffix..]) : string.Empty);
            }
        }

        var baseOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[0x10..]);
        var suffixOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(linkInfo[0x18..]);
        return ReadAnsiZ(linkInfo[baseOffset..]) + (suffixOffset > 0 ? ReadAnsiZ(linkInfo[suffixOffset..]) : string.Empty);
    }

    private static string ReadAnsiZ(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? data[..end] : data);
    }

    private static string ReadUnicodeZ(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
            {
                return Encoding.Unicode.GetString(data[..i]);
            }
        }

        return Encoding.Unicode.GetString(data);
    }
}
