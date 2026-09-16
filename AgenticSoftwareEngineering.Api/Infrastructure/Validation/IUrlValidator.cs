namespace AgenticSoftwareEngineering.Api.Infrastructure.Validation;

public interface IUrlValidator
{
    bool TryValidate(string? value, out Uri? uri);
}

public sealed class UrlValidator : IUrlValidator
{
    public bool TryValidate(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
