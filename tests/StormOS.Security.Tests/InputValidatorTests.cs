using StormOS.Security.Validation;

namespace StormOS.Security.Tests;

public sealed class InputValidatorTests
{
    [Theory]
    [InlineData("windows.game-mode", true)]
    [InlineData("steam:730", true)]
    [InlineData("Windows.GameMode", false)]
    [InlineData("../etc", false)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    public void Identifier(string value, bool expected) => Assert.Equal(expected, InputValidator.IsIdentifier(value));

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("dns.google", false)]
    [InlineData("1.1.1.1; rm -rf", false)]
    public void DnsServer(string value, bool expected) => Assert.Equal(expected, InputValidator.TryParseDnsServer(value, out _));

    [Theory]
    [InlineData("www.microsoft.com:443", true, "www.microsoft.com", 443)]
    [InlineData("[::1]:53", true, "::1", 53)]
    [InlineData("host:0", false, "", 0)]
    [InlineData("host:99999", false, "", 0)]
    [InlineData("noport", false, "", 0)]
    public void Endpoint(string value, bool expected, string host, int port)
    {
        Assert.Equal(expected, InputValidator.TryParseEndpoint(value, out var h, out var p));
        Assert.Equal(host, h);
        Assert.Equal(port, p);
    }

    [Fact]
    public void PathWithin_RejectsTraversal()
    {
        var root = Path.Combine(Path.GetTempPath(), "storm-root");
        Assert.True(InputValidator.IsPathWithin(Path.Combine(root, "a", "b.txt"), [root]));
        Assert.False(InputValidator.IsPathWithin(Path.Combine(root, "..", "evil.txt"), [root]));
        Assert.False(InputValidator.IsPathWithin("relative/path", [root]));
        Assert.False(InputValidator.IsPathWithin(root + "-sibling" + Path.DirectorySeparatorChar + "x", [root]));
    }

    [Theory]
    [InlineData("https://speed.cloudflare.com/__down?bytes=1", true)]
    [InlineData("http://example.com", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("file:///c:/windows", false)]
    public void HttpsUrl(string value, bool expected) => Assert.Equal(expected, InputValidator.IsHttpsUrl(value));

    [Fact]
    public void ParameterValue_RejectsControlCharacters()
    {
        Assert.True(InputValidator.IsSafeParameterValue("high-performance"));
        Assert.False(InputValidator.IsSafeParameterValue("a\nb"));
        Assert.False(InputValidator.IsSafeParameterValue(new string('x', 600)));
    }

    [Theory]
    [InlineData("1.1.1.1,1.0.0.1", true)]
    [InlineData("RegistryUserRun|Discord", true)]
    [InlineData("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", true)]
    [InlineData("x & del C:\\Windows", false)]
    [InlineData("a;b", false)]
    [InlineData("<script>", false)]
    [InlineData("line\nbreak", false)]
    [InlineData(null, false)]
    public void ParameterValuesRejectMetacharacters(string? value, bool expected) =>
        Assert.Equal(expected, InputValidator.IsSafeParameterValue(value));
}
