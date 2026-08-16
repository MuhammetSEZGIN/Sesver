using IdentityService.Attributes;

namespace IdentityServiceTests.UnitTests.Attributes;

public class ImageUrlAttributeTests
{
    private readonly ImageUrlAttribute _attribute = new();

    [Theory]
    [InlineData("https://example.com/background.png")]
    [InlineData("https://example.com/background.jpg")]
    [InlineData("https://example.com/background.jpeg")]
    [InlineData("https://example.com/background.webp")]
    [InlineData("https://example.com/background.gif")]
    [InlineData("https://cdn.example.com/media/abc123?format=gif")]
    public void IsValid_AcceptsHttpsImageUrls(string url)
    {
        Assert.True(_attribute.IsValid(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_AcceptsClearedValues(string? url)
    {
        Assert.True(_attribute.IsValid(url));
    }

    [Theory]
    [InlineData("http://example.com/background.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/gif;base64,R0lGODlhAQABAAAAACw=")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/background.png")]
    [InlineData("not a url")]
    public void IsValid_RejectsUnsafeOrRelativeValues(string url)
    {
        Assert.False(_attribute.IsValid(url));
    }

    [Fact]
    public void IsValid_RejectsUrlsLongerThanTheMaximum()
    {
        var url = "https://example.com/" + new string('a', 2048);

        Assert.False(_attribute.IsValid(url));
    }

    [Fact]
    public void IsValid_WithRequireHttpsDisabled_AcceptsHttp()
    {
        var attribute = new ImageUrlAttribute { RequireHttps = false };

        Assert.True(attribute.IsValid("http://example.com/background.png"));
    }
}
