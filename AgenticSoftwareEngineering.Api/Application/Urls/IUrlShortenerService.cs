using AgenticSoftwareEngineering.Api.Contracts.ShortLinks;

namespace AgenticSoftwareEngineering.Api.Application.Urls;

public interface IUrlShortenerService
{
    Task<ShortLinkResult> CreateAsync(string? targetUrl, CancellationToken cancellationToken);
    Task<RedirectResult> ResolveAsync(string shortCode, CancellationToken cancellationToken);
}

public abstract record ShortLinkResult
{
    public sealed record Created(ShortLinkResponse Response) : ShortLinkResult;
    public sealed record InvalidTarget : ShortLinkResult;
    public sealed record CollisionLimitExceeded(int Attempts) : ShortLinkResult;
}

public abstract record RedirectResult
{
    public sealed record Found(string TargetUrl) : RedirectResult;
    public sealed record Missing : RedirectResult;
}
