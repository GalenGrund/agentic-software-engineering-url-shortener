using AgenticSoftwareEngineering.Api.Domain.Orchestration;

namespace AgenticSoftwareEngineering.Api.Providers;

public sealed record AgentExecutionRequest(
    Guid WorkflowId,
    Guid NodeId,
    string TaskType,
    string Instructions,
    IReadOnlyList<string> UpstreamArtifactReferences,
    int Attempt,
    string? EffectiveRequirement = null,
    AgentExecutionMode Mode = AgentExecutionMode.Apply,
    Guid? AuthorizedChangeSetId = null,
    string? AuthorizedChangeSetFingerprint = null);

public enum AgentExecutionMode
{
    Proposal = 1,
    Apply = 2
}

public sealed record ProposedPrivilegedOperation(string OperationContent, string Summary, string Scope);

public sealed record AgentExecutionResponse(
    bool Succeeded,
    string Output,
    string ArtifactType,
    string ContentReference,
    string ContentHash,
    string? FailureReason = null,
    FailureClassification FailureClassification = FailureClassification.Permanent,
    ProposedPrivilegedOperation? ProposedOperation = null);

public interface IAgentProvider
{
    string Name { get; }
    IReadOnlySet<string> CompatibleFallbackProviders { get; }
    Task<AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class DeterministicAgentProvider : IAgentProvider
{
    public string Name => "deterministic";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(new[] { "deterministic-fallback" }, StringComparer.OrdinalIgnoreCase);

    public Task<AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        var output = $"Deterministic output for {request.TaskType}.";
        var artifactType = request.TaskType switch
        {
            "normalize-requirement" => "normalized-requirement",
            "decompose-plan" => "plan-decomposition",
            "architecture-design" => "architecture-design",
            "implementation-preparation" => "implementation-preparation",
            "validation" => "validation-evidence",
            "release-readiness" => "release-readiness-evidence",
            _ => "engineering-output"
        };
        var reference = $"deterministic://{request.TaskType}/{request.WorkflowId:N}/{request.Attempt}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(output)));
        var proposedOperation = request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Proposal
            ? new ProposedPrivilegedOperation("prepare-approved-implementation-context", "Prepare the approved implementation context.", "implementation-preparation")
            : null;
        return Task.FromResult(new AgentExecutionResponse(true, output, artifactType, reference, hash, ProposedOperation: proposedOperation));
    }
}

public sealed class DeterministicFallbackProvider : IAgentProvider
{
    public string Name => "deterministic-fallback";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public Task<AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
}
