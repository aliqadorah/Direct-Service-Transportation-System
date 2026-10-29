using System.Security.Cryptography;
using DirectService.Shared.Infrastructure.Security;

namespace DirectService.Transportation.Infrastructure.Tests.Security;

/// <summary>The URL token key check that runs at start-up: a 32-byte base64 key passes; anything else fails with how to set one.</summary>
public sealed class UrlTokenOptionsTests
{
    [Fact]
    public void A_32_byte_key_passes()
    {
        Assert.Null(UrlTokenOptions.Problem(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        Assert.True(new UrlTokenOptionsValidator().Validate(null, new UrlTokenOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }).Succeeded);
    }

    [Theory]
    [InlineData("", "is not set")]
    [InlineData("not base64 !", "is not valid base64")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==", "is 16 bytes")]
    public void Other_keys_fail_with_how_to_fix_it(string key, string what)
    {
        var problem = UrlTokenOptions.Problem(key);
        Assert.NotNull(problem);
        Assert.Contains(what, problem, StringComparison.Ordinal);
        Assert.Contains("exactly 32 bytes", UrlTokenOptions.Problem("AAAAAAAAAAAAAAAAAAAAAA==")!, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set UrlTokens:Key", problem, StringComparison.Ordinal);
        Assert.Contains("UrlTokens__Key", problem, StringComparison.Ordinal);
        Assert.False(new UrlTokenOptionsValidator().Validate(null, new UrlTokenOptions { Key = key }).Succeeded);
    }
}
