using AgenticSoftwareEngineering.Api.Infrastructure.Validation;

namespace AgenticSoftwareEngineering.Tests;

public sealed class UrlValidatorTests
{
    private readonly UrlValidator validator = new();

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:8080/path")]
    public void TryValidate_AcceptsAbsoluteHttpUrls(string value)
    {
        Assert.True(validator.TryValidate(value, out var uri));
        Assert.NotNull(uri);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    [InlineData(null)]
    public void TryValidate_RejectsUnsupportedOrRelativeUrls(string? value)
    {
        Assert.False(validator.TryValidate(value, out _));
    }
}
