using System.Buffers.Binary;
using System.Net;
using StormOS.Core.Network;
using StormOS.Core.Scoring;
using StormOS.Network.Diagnostics;
using StormOS.Network.Dns;

namespace StormOS.Network.Tests;

public class DnsMessageTests
{
    [Fact]
    public void BuildQuery_EncodesHeaderAndLabels()
    {
        var query = DnsMessage.BuildQuery(0x1234, "www.example.com");

        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(query));
        Assert.Equal(0x0100, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)));
        Assert.Equal(3, query[12]);
        Assert.Equal((byte)'w', query[13]);
        Assert.Equal(12 + 17 + 4, query.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a..b")]
    [InlineData("this-label-is-way-too-long-for-dns-because-it-exceeds-sixty-three-chars.com")]
    public void BuildQuery_RejectsInvalidHosts(string host) =>
        Assert.ThrowsAny<ArgumentException>(() => DnsMessage.BuildQuery(1, host));

    [Fact]
    public void Parse_ReadsCompressedARecords()
    {
        var query = DnsMessage.BuildQuery(7, "example.com");
        var response = new List<byte>(query);
        response[2] = 0x81;
        response[3] = 0x80;
        response[7] = 2;
        foreach (var address in new[] { new byte[] { 93, 184, 216, 34 }, [1, 2, 3, 4] })
        {
            response.AddRange([0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x0E, 0x10, 0x00, 0x04]);
            response.AddRange(address);
        }

        var parsed = DnsMessage.Parse(response.ToArray());

        Assert.Equal(7, parsed.Id);
        Assert.Equal(0, parsed.ResponseCode);
        Assert.Equal([IPAddress.Parse("93.184.216.34"), IPAddress.Parse("1.2.3.4")], parsed.Addresses);
        Assert.False(parsed.Truncated);
    }

    [Fact]
    public void Parse_ReportsNxDomain()
    {
        var response = DnsMessage.BuildQuery(9, "missing.example");
        response[2] = 0x81;
        response[3] = 0x83;

        var parsed = DnsMessage.Parse(response);

        Assert.Equal(3, parsed.ResponseCode);
        Assert.Equal("NXDOMAIN", DnsMessage.DescribeResponseCode(parsed.ResponseCode));
        Assert.Empty(parsed.Addresses);
    }

    [Fact]
    public void Parse_RejectsQueriesAndTruncatedMessages()
    {
        var query = DnsMessage.BuildQuery(1, "example.com");
        Assert.Throws<FormatException>(() => DnsMessage.Parse(query));
        Assert.Throws<FormatException>(() => DnsMessage.Parse(new byte[5]));

        var response = new List<byte>(query);
        response[2] = 0x81;
        response[7] = 1;
        response.AddRange([0xC0, 0x0C, 0x00, 0x01, 0x00, 0x01]);
        Assert.Throws<FormatException>(() => DnsMessage.Parse(response.ToArray()));
    }
}

public class LatencyMathTests
{
    [Fact]
    public void Jitter_IsMeanAbsoluteDifferenceOfConsecutiveReplies()
    {
        double?[] samples = [10, 14, 12, null, 20, 21];

        Assert.Equal((4 + 2 + 1) / 3.0, LatencyMath.Jitter(samples)!.Value, 6);
    }

    [Fact]
    public void Jitter_IsUnavailableWithoutConsecutiveReplies()
    {
        Assert.Null(LatencyMath.Jitter([10, null, 12]));
        Assert.Null(LatencyMath.Jitter([]));
    }

    [Fact]
    public void Summarize_CountsLossAndNeverInventsValues()
    {
        var stats = LatencyMath.Summarize("1.1.1.1", [null, null, null, null]);

        Assert.Equal(4, stats.Sent);
        Assert.Equal(0, stats.Received);
        Assert.Equal(100, stats.LossPercent);
        Assert.Null(stats.AverageMs);
        Assert.Null(stats.MinMs);
        Assert.Null(stats.JitterMs);
    }

    [Fact]
    public void Summarize_ComputesStatistics()
    {
        var stats = LatencyMath.Summarize("host", [10, 20, null, 30]);

        Assert.Equal(25, stats.LossPercent);
        Assert.Equal(10, stats.MinMs);
        Assert.Equal(30, stats.MaxMs);
        Assert.Equal(20, stats.AverageMs);
    }

    [Fact]
    public void NetworkScore_UsesOnlyMeasuredInputs()
    {
        var good = LatencyMath.Summarize("host", Enumerable.Repeat<double?>(8, 20).ToList());
        var dns = new[] { new DnsTestResult { Server = "1.1.1.1", Success = true, LatencyMs = 9, IsConfigured = true } };

        var score = StormScores.Network(good, dns);
        Assert.Equal(100, score.Score);

        var unreachable = LatencyMath.Summarize("host", [null, null]);
        var none = StormScores.Network(unreachable, []);
        Assert.Equal(0.30, none.Coverage, 3);
        Assert.Null(none.Score);
        Assert.Equal("Insufficient data", none.Rating);
    }
}
