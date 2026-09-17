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

    [Fact]
    public async Task Gate2WorkflowApiExposesPersistedDagParallelReadyStateAndCompletionEvidence()
    {
        using var create = await client.PostAsJsonAsync("/api/workflows/greenfield", new { requirement = "Create a short URL" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var workflowId = created.RootElement.GetProperty("workflowId").GetGuid();

        await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        using var parallelResponse = await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        using var parallel = JsonDocument.Parse(await parallelResponse.Content.ReadAsStringAsync());
        var readyTaskTypes = parallel.RootElement.GetProperty("nodes").EnumerateArray()
            .Where(node => node.GetProperty("state").GetString() == "Ready")
            .Select(node => node.GetProperty("taskType").GetString())
            .ToList();
        Assert.Contains("architecture-design", readyTaskTypes);
        Assert.Contains("implementation-preparation", readyTaskTypes);
        Assert.Equal(6, parallel.RootElement.GetProperty("dependencies").GetArrayLength());

        JsonDocument? final = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            final?.Dispose();
            var response = await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
            final = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (final.RootElement.GetProperty("state").GetString() == "Completed")
            {
                break;
            }
        }

        using (final)
        {
            Assert.Equal("Completed", final!.RootElement.GetProperty("state").GetString());
            Assert.Equal(6, final.RootElement.GetProperty("executions").GetArrayLength());
            Assert.Equal(6, final.RootElement.GetProperty("artifacts").GetArrayLength());
            Assert.Contains(final.RootElement.GetProperty("validations").EnumerateArray(), validation =>
                validation.GetProperty("validationName").GetString() == "release-readiness" && validation.GetProperty("passed").GetBoolean());
            Assert.Contains(final.RootElement.GetProperty("events").EnumerateArray(), eventItem =>
                eventItem.GetProperty("eventType").GetString() == "ReleaseReadinessEvaluated");
        }
    }

    [Fact]
    public async Task Gate3ApprovalApiCanRejectHighRiskWorkflow()
    {
        using var create = await client.PostAsJsonAsync("/api/workflows/greenfield", new { requirement = "Create a short URL", requiresHighRiskApproval = true });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var workflowId = created.RootElement.GetProperty("workflowId").GetGuid();
        await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        var waiting = await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        using var waitingJson = JsonDocument.Parse(await waiting.Content.ReadAsStringAsync());
        var approvalId = waitingJson.RootElement.GetProperty("approvals").EnumerateArray().Single().GetProperty("id").GetGuid();

        var decision = await client.PostAsJsonAsync($"/api/workflows/{workflowId}/approvals/{approvalId}/decision", new { approved = false, rationale = "Rejected for review" });
        using var rejected = JsonDocument.Parse(await decision.Content.ReadAsStringAsync());

        Assert.Equal("SafeStopped", rejected.RootElement.GetProperty("state").GetString());
        Assert.Contains(rejected.RootElement.GetProperty("events").EnumerateArray(), item => item.GetProperty("eventType").GetString() == "HumanEscalationRequired");
    }

    [Fact]
    public async Task Gate3ApprovalApiAuthorizesHighRiskExecutionWithPlanRevisionEvidence()
    {
        using var create = await client.PostAsJsonAsync("/api/workflows/greenfield", new { requirement = "Create a short URL", requiresHighRiskApproval = true });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var workflowId = created.RootElement.GetProperty("workflowId").GetGuid();
        await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        await client.PostAsync($"/api/workflows/{workflowId}/advance", null);
        using var waiting = JsonDocument.Parse(await (await client.PostAsync($"/api/workflows/{workflowId}/advance", null)).Content.ReadAsStringAsync());
        var approval = waiting.RootElement.GetProperty("approvals").EnumerateArray().Single();
        var planRevisionId = approval.GetProperty("planRevisionId").GetGuid();
        var approvalId = approval.GetProperty("id").GetGuid();

        using var decision = JsonDocument.Parse(await (await client.PostAsJsonAsync($"/api/workflows/{workflowId}/approvals/{approvalId}/decision", new { approved = true, rationale = "Approved for controlled integration test" })).Content.ReadAsStringAsync());

        Assert.Equal(planRevisionId, decision.RootElement.GetProperty("approvals").EnumerateArray().Single().GetProperty("planRevisionId").GetGuid());
        Assert.Contains(decision.RootElement.GetProperty("policies").EnumerateArray(), policy =>
            policy.GetProperty("policyName").GetString() == "gate3-post-approval-authorization" && policy.GetProperty("allowed").GetBoolean());
        var events = decision.RootElement.GetProperty("events").EnumerateArray().ToList();
        var authorizationIndex = events.FindIndex(item => item.GetProperty("eventType").GetString() == "PostApprovalAuthorizationAllowed");
        var executionIndex = events.FindIndex(item => item.GetProperty("eventType").GetString() == "NodeExecutionStarted" && item.GetProperty("details").GetString() == "Implementation Preparation");
        Assert.True(authorizationIndex >= 0 && authorizationIndex < executionIndex);
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