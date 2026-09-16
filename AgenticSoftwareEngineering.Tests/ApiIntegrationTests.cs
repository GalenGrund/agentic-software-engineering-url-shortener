using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AgenticSoftwareEngineering.Tests;

public sealed class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient client;

    public ApiIntegrationTests(ApiFactory factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task CreateResponseShortUrlIsDirectlyResolvable()
    {
        using var create = await client.PostAsJsonAsync("/api/short-links", new { targetUrl = "https://example.com/integration" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var payload = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shortUrl = payload.RootElement.GetProperty("shortUrl").GetString();
        Assert.NotNull(shortUrl);

        var shortPath = new Uri(shortUrl!).PathAndQuery;
        using var redirect = await client.GetAsync(shortPath);

        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/integration", redirect.Headers.Location?.ToString());
    }

    [Fact]
    public async Task InvalidCreateReturnsStructuredBadRequest()
    {
        using var response = await client.PostAsJsonAsync("/api/short-links", new { targetUrl = "javascript:alert(1)" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("invalid_target_url", error?.Code);
    }

    [Fact]
    public async Task MissingPublicCodeReturnsStructuredNotFound()
    {
        using var response = await client.GetAsync("/missing-public-code");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("short_link_not_found", error?.Code);
    }

    private sealed record ApiError(string Code, string Message);
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"agentic-gate1-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={databasePath}",
                ["PublicBaseUrl"] = "http://localhost"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        TryDelete(databasePath);
        TryDelete(databasePath + "-shm");
        TryDelete(databasePath + "-wal");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}