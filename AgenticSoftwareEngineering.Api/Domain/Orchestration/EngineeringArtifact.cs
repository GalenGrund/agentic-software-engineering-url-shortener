namespace AgenticSoftwareEngineering.Api.Domain.Orchestration;

public enum ArtifactValidationStatus
{
    Unvalidated = 1,
    Valid = 2,
    Invalid = 3
}

public sealed class EngineeringArtifact
{
    private EngineeringArtifact()
    {
    }

    private EngineeringArtifact(
        Guid workflowId,
        string artifactType,
        int version,
        string contentReference,
        string contentHash,
        Guid? producerNodeId,
        Guid? producerExecutionId,
        Guid? supersedesArtifactId,
        DateTimeOffset createdAtUtc)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        }

        if (string.IsNullOrWhiteSpace(artifactType)) throw new ArgumentException("Artifact type is required.", nameof(artifactType));
        if (string.IsNullOrWhiteSpace(contentReference)) throw new ArgumentException("Content reference is required.", nameof(contentReference));
        if (string.IsNullOrWhiteSpace(contentHash)) throw new ArgumentException("Content hash is required.", nameof(contentHash));

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (supersedesArtifactId == Guid.Empty)
        {
            throw new ArgumentException("A supersession identity must be a non-empty id.", nameof(supersedesArtifactId));
        }

        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        ArtifactType = artifactType;
        Version = version;
        ContentReference = contentReference;
        ContentHash = contentHash;
        ProducerNodeId = producerNodeId;
        ProducerExecutionId = producerExecutionId;
        SupersedesArtifactId = supersedesArtifactId;
        CreatedAtUtc = createdAtUtc;
        ValidationStatus = ArtifactValidationStatus.Unvalidated;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string ArtifactType { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public string ContentReference { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public Guid? ProducerNodeId { get; private set; }
    public Guid? ProducerExecutionId { get; private set; }
    public Guid? SupersedesArtifactId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public ArtifactValidationStatus ValidationStatus { get; private set; }

    public static EngineeringArtifact Create(
        Guid workflowId,
        string artifactType,
        int version,
        string contentReference,
        string contentHash,
        Guid? producerNodeId,
        Guid? producerExecutionId,
        Guid? supersedesArtifactId,
        DateTimeOffset createdAtUtc) =>
        new(workflowId, artifactType, version, contentReference, contentHash, producerNodeId, producerExecutionId, supersedesArtifactId, createdAtUtc);

    public static EngineeringArtifact Revise(EngineeringArtifact prior, string contentReference, string contentHash, DateTimeOffset createdAtUtc) =>
        new(prior.WorkflowId, prior.ArtifactType, prior.Version + 1, contentReference, contentHash, prior.ProducerNodeId, prior.ProducerExecutionId, prior.Id, createdAtUtc);

    public void SetValidationStatus(ArtifactValidationStatus status) => ValidationStatus = status;
}
