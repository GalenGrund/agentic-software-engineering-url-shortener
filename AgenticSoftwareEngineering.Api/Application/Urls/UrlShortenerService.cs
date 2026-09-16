using AgenticSoftwareEngineering.Api.Contracts.ShortLinks;
using AgenticSoftwareEngineering.Api.Domain.Urls;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Infrastructure.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Api.Application.Urls;

public sealed class UrlShortenerService(
    AppDbContext db,
    IUrlValidator validator,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IShortCodeGenerator codeGenerator) : IUrlShortenerService
{
    public async Task<ShortLinkResult> CreateAsync(string? targetUrl, CancellationToken cancellationToken)
    {
        if (!validator.TryValidate(targetUrl, out var uri))
        {
            return new ShortLinkResult.InvalidTarget();
        }

        var maxCollisionAttempts = Math.Max(1, configuration.GetValue("ShortCode:MaxCollisionAttempts", 5));
        for (var attempt = 1; attempt <= maxCollisionAttempts; attempt++)
        {
            var shortCode = codeGenerator.Generate();
            var createdAt = timeProvider.GetUtcNow();
            var link = new ShortLink(shortCode, uri!, createdAt);
            db.ShortLinks.Add(link);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new ShortLinkResult.Created(ToResponse(link));
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintCollision(exception))
            {
                db.Entry(link).State = EntityState.Detached;
                if (attempt == maxCollisionAttempts)
                {
                    return new ShortLinkResult.CollisionLimitExceeded(maxCollisionAttempts);
                }
            }
        }

        throw new InvalidOperationException("Short-link creation attempts unexpectedly exhausted.");
    }

    public async Task<RedirectResult> ResolveAsync(string shortCode, CancellationToken cancellationToken)
    {
        var link = await db.ShortLinks.SingleOrDefaultAsync(item => item.ShortCode == shortCode && item.IsActive, cancellationToken);
        if (link is null)
        {
            return new RedirectResult.Missing();
        }

        db.ClickEvents.Add(new ClickEvent(link.Id, timeProvider.GetUtcNow(), RedirectOutcome.Succeeded));
        await db.SaveChangesAsync(cancellationToken);
        return new RedirectResult.Found(link.TargetUrl);
    }

    private ShortLinkResponse ToResponse(ShortLink link)
    {
        var baseUrl = configuration["PublicBaseUrl"]?.TrimEnd('/') ?? "https://localhost:5001";
        return new ShortLinkResponse(link.Id, link.ShortCode, link.TargetUrl, $"{baseUrl}/{link.ShortCode}", link.CreatedAtUtc);
    }

    private static bool IsUniqueConstraintCollision(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException { SqliteErrorCode: 19 };
    }
}
