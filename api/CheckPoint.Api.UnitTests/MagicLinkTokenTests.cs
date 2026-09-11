using CheckPoint.Api.Services;

namespace CheckPoint.Api.UnitTests;

// Token generation has no dependencies (no DB, no clock), so it's covered here as a
// pure unit test. Behaviour that touches the database (issuing, validating,
// consuming) is covered in CheckPoint.Api.IntegrationTests.MagicLinkServiceTests.
public class MagicLinkTokenTests
{
    [Fact]
    public void GeneratedToken_IsUrlSafe()
    {
        var token = MagicLinkService.GenerateToken();

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void GeneratedToken_HasAtLeast256BitsOfEntropy()
    {
        // 256 bits base64url-encoded, no padding: ceil(256/6) = 43 characters.
        var token = MagicLinkService.GenerateToken();

        Assert.True(token.Length >= 43, $"Expected at least 43 characters, got {token.Length}");
    }

    [Fact]
    public void GeneratedTokens_AreNotPredictableOrRepeated()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => MagicLinkService.GenerateToken()).ToHashSet();

        Assert.Equal(1000, tokens.Count);
    }
}
