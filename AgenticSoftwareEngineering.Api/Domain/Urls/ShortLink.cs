namespace AgenticSoftwareEngineering.Api.Domain.Urls;

public sealed class ShortLink
{
    private ShortLink()
    {
    }

    public ShortLink(string shortCode, Uri targetUrl, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(shortCode))
        {
            throw new ArgumentException("A short code is required.", nameof(shortCode));
        }

        Id = Guid.NewGuid();
        ShortCode = shortCode;
        TargetUrl = targetUrl.ToString();
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string ShortCode { get; private set; } = string.Empty;
    public string TargetUrl { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public ICollection<ClickEvent> Clicks { get; private set; } = new List<ClickEvent>();
}
