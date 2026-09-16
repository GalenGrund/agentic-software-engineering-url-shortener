using AgenticSoftwareEngineering.Api.Application.Urls;
using AgenticSoftwareEngineering.Api.Contracts.Errors;
using AgenticSoftwareEngineering.Api.Contracts.ShortLinks;
using Microsoft.AspNetCore.Mvc;
using UrlRedirectResult = AgenticSoftwareEngineering.Api.Application.Urls.RedirectResult;

namespace AgenticSoftwareEngineering.Api.Controllers;

[ApiController]
[Route("api/short-links")]
public sealed class ShortLinksController(IUrlShortenerService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ShortLinkResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateShortLinkRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request.TargetUrl, cancellationToken);
        return result switch
        {
            ShortLinkResult.Created created => CreatedAtAction(nameof(Redirect), new { shortCode = created.Response.ShortCode }, created.Response),
            ShortLinkResult.InvalidTarget => BadRequest(new ApiErrorResponse("invalid_target_url", "TargetUrl must be an absolute HTTP or HTTPS URL.")),
            ShortLinkResult.CollisionLimitExceeded collision => StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiErrorResponse("short_code_unavailable", $"A unique short code was not available after {collision.Attempts} attempts.")),
            _ => Problem("The short link could not be created.")
        };
    }

    [HttpGet("/{shortCode}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Redirect(string shortCode, CancellationToken cancellationToken)
    {
        var result = await service.ResolveAsync(shortCode, cancellationToken);
        return result switch
        {
            UrlRedirectResult.Found found => Redirect(found.TargetUrl),
            UrlRedirectResult.Missing => NotFound(new ApiErrorResponse("short_link_not_found", "The requested short code does not exist.")),
            _ => Problem("The short link could not be resolved.")
        };
    }
}
