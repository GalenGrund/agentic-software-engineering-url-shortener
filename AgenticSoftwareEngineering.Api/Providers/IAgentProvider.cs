namespace AgenticSoftwareEngineering.Api.Providers;

public interface IAgentProvider
{
    string Name { get; }
    IReadOnlySet<string> CompatibleFallbackProviders { get; }
}

public sealed class DeterministicAgentProvider : IAgentProvider
{
    public string Name => "deterministic";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
