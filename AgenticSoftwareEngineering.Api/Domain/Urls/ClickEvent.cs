namespace AgenticSoftwareEngineering.Api.Domain.Urls;

public sealed class ClickEvent
{
    private ClickEvent()
    {
    }

    public ClickEvent(Guid shortLinkId, DateTimeOffset occurredAtUtc, RedirectOutcome outcome)
    {
        Id = Guid.NewGuid();
        ShortLinkId = shortLinkId;
        OccurredAtUtc = occurredAtUtc;
        Outcome = outcome;
    }

    public Guid Id { get; private set; }
    public Guid ShortLinkId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public RedirectOutcome Outcome { get; private set; }
    public ShortLink? ShortLink { get; private set; }
}

public enum RedirectOutcome
{
    Succeeded = 1,
    NotFound = 2
}
