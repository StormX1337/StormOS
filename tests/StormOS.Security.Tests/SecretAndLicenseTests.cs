using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StormOS.Core.Licensing;
using StormOS.Security.Integrity;
using StormOS.Security.Licensing;
using StormOS.Security.Secrets;

namespace StormOS.Security.Tests;

public sealed class SecretAndLicenseTests
{
    [Theory]
    [InlineData("Authorization: Bearer abc.def-123", "Bearer ***")]
    [InlineData("password=hunter2 next", "password=*** next")]
    [InlineData("token: 'xyz'", "token: ***")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxIn0.c2ln", "***")]
    public void Redact_MasksSecrets(string input, string expectedFragment)
    {
        var redacted = SecretRedactor.Redact(input);
        Assert.Contains(expectedFragment, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RefreshToken", true)]
    [InlineData("client_secret", true)]
    [InlineData("GameName", false)]
    public void SensitiveNames(string name, bool expected) => Assert.Equal(expected, SecretRedactor.IsSensitiveName(name));

    [Fact]
    public async Task FileIntegrity_VerifiesDigest()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "storm", TestContext.Current.CancellationToken);
            var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("storm")));
            Assert.True(await FileIntegrity.VerifySha256Async(path, digest, TestContext.Current.CancellationToken));
            Assert.False(await FileIntegrity.VerifySha256Async(path, new string('0', 64), TestContext.Current.CancellationToken));
            Assert.False(await FileIntegrity.VerifySha256Async(path, "not-hex", TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EntitlementToken_ValidSignature_GrantsFeatures()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new EntitlementTokenVerifier(key.ExportSubjectPublicKeyInfoPem());
        var token = Sign(key, new { iss = "storm-cloud", did = "device-1", tier = "pro", features = new[] { Features.Benchmark, Features.Overlay }, exp = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds() });

        var result = verifier.Verify(token, "device-1");

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(LicenseTier.Pro, result.Value!.Tier);
        Assert.True(result.Value.Has(Features.Overlay));
        Assert.False(result.Value.Has(Features.AiAnalysis));
    }

    [Fact]
    public void EntitlementToken_Rejects_TamperedExpiredOrForeign()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var verifier = new EntitlementTokenVerifier(key.ExportSubjectPublicKeyInfoPem());
        var claims = new { iss = "storm-cloud", did = "device-1", tier = "ultimate", features = new[] { "x" }, exp = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds() };

        Assert.False(verifier.Verify(Sign(other, claims), "device-1").IsSuccess);
        Assert.False(verifier.Verify(Sign(key, claims), "device-2").IsSuccess);
        Assert.False(verifier.Verify(Sign(key, new { iss = "storm-cloud", did = "device-1", tier = "pro", features = Array.Empty<string>(), exp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds() }), "device-1").IsSuccess);
        Assert.False(verifier.Verify("garbage", "device-1").IsSuccess);

        var valid = Sign(key, claims);
        var parts = valid.Split('.');
        var tampered = parts[0] + "." + parts[1].Replace('A', 'B') + "." + parts[2];
        Assert.False(verifier.Verify(tampered, "device-1").IsSuccess);
    }

    private static string Sign(ECDsa key, object claims)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = key.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return header + "." + payload + "." + B64(signature);
    }
}
