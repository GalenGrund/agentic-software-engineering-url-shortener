namespace AgenticSoftwareEngineering.Api.Contracts.ShortLinks;

public sealed record ShortLinkResponse(
    Guid Id,
    string ShortCode,
    string TargetUrl,
    string ShortUrl,
    DateTimeOffset CreatedAtUtc);
