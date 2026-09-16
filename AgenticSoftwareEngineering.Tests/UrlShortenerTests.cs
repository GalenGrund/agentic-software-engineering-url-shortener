using AgenticSoftwareEngineering.Api.Application.Urls;
using AgenticSoftwareEngineering.Api.Domain.Urls;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Infrastructure.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AgenticSoftwareEngineering.Tests;

public sealed class UrlShortenerTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly AppDbContext db;
    private readonly UrlShortenerService service;

    public UrlShortenerTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        db = new AppDbContext(options);
        db.Database.Migrate();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicBaseUrl"] = "https://localhost:5001" })
            .Build();
        service = new UrlShortenerService(db, new UrlValidator(), configuration, new FixedTimeProvider(), new SecureShortCodeGenerator());
    }

    [Fact]
    public async Task CreateAsync_ValidUrl_ReturnsGeneratedCodeAndPersistsLink()
    {
        var result = await service.CreateAsync("https://example.com/docs", CancellationToken.None);

        var created = Assert.IsType<ShortLinkResult.Created>(result);
        Assert.Equal(7, created.Response.ShortCode.Length);
        Assert.Equal("https://example.com/docs", created.Response.TargetUrl);
        Assert.Equal(1, await db.ShortLinks.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_InvalidUrl_ReturnsValidationFailureWithoutPersistence()
    {
        var result = await service.CreateAsync("javascript:alert(1)", CancellationToken.None);

        Assert.IsType<ShortLinkResult.InvalidTarget>(result);
        Assert.Empty(await db.ShortLinks.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniqueCodes()
    {
        var first = Assert.IsType<ShortLinkResult.Created>(await service.CreateAsync("https://example.com/one", CancellationToken.None));
        var second = Assert.IsType<ShortLinkResult.Created>(await service.CreateAsync("https://example.com/two", CancellationToken.None));

        Assert.NotEqual(first.Response.ShortCode, second.Response.ShortCode);
    }

    [Fact]
    public async Task CreateAsync_RetriesAfterUniqueCollision()
    {
        var generator = new SequenceCodeGenerator("fixed", "fixed", "next");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://localhost:5001",
                ["ShortCode:MaxCollisionAttempts"] = "3"
            })
            .Build();
        var retryingService = new UrlShortenerService(db, new UrlValidator(), configuration, new FixedTimeProvider(), generator);

        await retryingService.CreateAsync("https://example.com/first", CancellationToken.None);
        var result = await retryingService.CreateAsync("https://example.com/second", CancellationToken.None);

        var created = Assert.IsType<ShortLinkResult.Created>(result);
        Assert.Equal("next", created.Response.ShortCode);
        Assert.Equal(3, generator.Calls);
    }

    [Fact]
    public async Task CreateAsync_ReturnsCollisionLimitWhenAttemptsAreExhausted()
    {
        var generator = new SequenceCodeGenerator("fixed", "fixed", "fixed");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://localhost:5001",
                ["ShortCode:MaxCollisionAttempts"] = "2"
            })
            .Build();
        var boundedService = new UrlShortenerService(db, new UrlValidator(), configuration, new FixedTimeProvider(), generator);

        await boundedService.CreateAsync("https://example.com/first", CancellationToken.None);
        var result = await boundedService.CreateAsync("https://example.com/second", CancellationToken.None);

        var exhausted = Assert.IsType<ShortLinkResult.CollisionLimitExceeded>(result);
        Assert.Equal(2, exhausted.Attempts);
        Assert.Equal(3, generator.Calls);
    }

    [Fact]
    public async Task ResolveAsync_ActiveLink_ReturnsTargetAndCapturesAnalytics()
    {
        var created = Assert.IsType<ShortLinkResult.Created>(await service.CreateAsync("https://example.com/redirect", CancellationToken.None));

        var result = await service.ResolveAsync(created.Response.ShortCode, CancellationToken.None);

        var found = Assert.IsType<RedirectResult.Found>(result);
        Assert.Equal("https://example.com/redirect", found.TargetUrl);
        var click = await db.ClickEvents.SingleAsync();
        Assert.Equal(created.Response.Id, click.ShortLinkId);
        Assert.Equal(RedirectOutcome.Succeeded, click.Outcome);
        Assert.NotEqual(default, click.OccurredAtUtc);
    }

    [Fact]
    public async Task ResolveAsync_MissingCode_ReturnsMissingWithoutAnalytics()
    {
        var result = await service.ResolveAsync("missing", CancellationToken.None);

        Assert.IsType<RedirectResult.Missing>(result);
        Assert.Empty(await db.ClickEvents.ToListAsync());
    }

    [Fact]
    public void ClickEvent_DoesNotPersistClientIp()
    {
        Assert.Null(typeof(ClickEvent).GetProperty("SourceIp"));
        Assert.Null(typeof(ClickEvent).GetProperty("ClientIp"));
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset current = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => current;
}

internal sealed class SequenceCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    private readonly Queue<string> sequence = new(codes);
    public int Calls { get; private set; }

    public string Generate()
    {
        Calls++;
        return sequence.Count > 0 ? sequence.Dequeue() : codes[^1];
    }
}
