using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace StormOS.Network.Dns;

/// <summary>Result of parsing a DNS response.</summary>
/// <param name="Id">Transaction id.</param>
/// <param name="ResponseCode">RCODE (0 = NOERROR, 3 = NXDOMAIN …).</param>
/// <param name="Addresses">A/AAAA answers.</param>
/// <param name="Truncated">Whether the TC bit was set.</param>
public sealed record DnsResponse(ushort Id, int ResponseCode, IReadOnlyList<IPAddress> Addresses, bool Truncated);

/// <summary>Minimal RFC 1035 message codec for A/AAAA lookups against a specific server.</summary>
public static class DnsMessage
{
    /// <summary>Query type A.</summary>
    public const ushort TypeA = 1;

    /// <summary>Query type AAAA.</summary>
    public const ushort TypeAaaa = 28;

    /// <summary>Builds a recursive query for one host name.</summary>
    /// <param name="id">Transaction id.</param>
    /// <param name="host">Host name.</param>
    /// <param name="type">Query type.</param>
    /// <returns>The encoded message.</returns>
    /// <exception cref="ArgumentException">The host name is not valid.</exception>
    public static byte[] BuildQuery(ushort id, string host, ushort type = TypeA)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var labels = host.TrimEnd('.').Split('.');
        if (host.Length > 253 || labels.Any(l => l.Length is 0 or > 63))
        {
            throw new ArgumentException("Invalid host name.", nameof(host));
        }

        var buffer = new List<byte>(host.Length + 18);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100);
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);
        buffer.AddRange(header.ToArray());
        foreach (var label in labels)
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buffer.Add((byte)bytes.Length);
            buffer.AddRange(bytes);
        }

        buffer.Add(0);
        buffer.Add((byte)(type >> 8));
        buffer.Add((byte)type);
        buffer.Add(0);
        buffer.Add(1);
        return [.. buffer];
    }

    /// <summary>Parses a response.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The parsed response.</returns>
    /// <exception cref="FormatException">The message is malformed.</exception>
    public static DnsResponse Parse(ReadOnlySpan<byte> message)
    {
        if (message.Length < 12)
        {
            throw new FormatException("DNS message is shorter than its header.");
        }

        var id = BinaryPrimitives.ReadUInt16BigEndian(message);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(message[2..]);
        if ((flags & 0x8000) == 0)
        {
            throw new FormatException("The message is not a response.");
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        var answers = BinaryPrimitives.ReadUInt16BigEndian(message[6..]);
        var offset = 12;
        for (var i = 0; i < questions; i++)
        {
            offset = SkipName(message, offset) + 4;
        }

        var addresses = new List<IPAddress>();
        for (var i = 0; i < answers; i++)
        {
            offset = SkipName(message, offset);
            EnsureAvailable(message, offset, 10);
            var type = BinaryPrimitives.ReadUInt16BigEndian(message[offset..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 8)..]);
            offset += 10;
            EnsureAvailable(message, offset, length);
            if (type == TypeA && length == 4 || type == TypeAaaa && length == 16)
            {
                addresses.Add(new IPAddress(message.Slice(offset, length)));
            }

            offset += length;
        }

        return new DnsResponse(id, flags & 0x000F, addresses, (flags & 0x0200) != 0);
    }

    /// <summary>Describes an RCODE.</summary>
    /// <param name="code">Response code.</param>
    /// <returns>A readable name.</returns>
    public static string DescribeResponseCode(int code) => code switch
    {
        0 => "NOERROR",
        1 => "FORMERR",
        2 => "SERVFAIL",
        3 => "NXDOMAIN",
        4 => "NOTIMP",
        5 => "REFUSED",
        _ => $"RCODE {code}",
    };

    private static int SkipName(ReadOnlySpan<byte> message, int offset)
    {
        for (var guard = 0; guard < 128; guard++)
        {
            EnsureAvailable(message, offset, 1);
            var length = message[offset];
            if (length == 0)
            {
                return offset + 1;
            }

            if ((length & 0xC0) == 0xC0)
            {
                EnsureAvailable(message, offset, 2);
                return offset + 2;
            }

            offset += length + 1;
        }

        throw new FormatException("DNS name is too long.");
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> message, int offset, int count)
    {
        if (offset < 0 || offset + count > message.Length)
        {
            throw new FormatException("DNS message is truncated.");
        }
    }
}
