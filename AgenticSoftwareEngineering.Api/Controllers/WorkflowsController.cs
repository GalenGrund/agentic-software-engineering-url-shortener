using AgenticSoftwareEngineering.Api.Application.Orchestration;
using Microsoft.AspNetCore.Mvc;

namespace AgenticSoftwareEngineering.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController(IOrchestrationService service) : ControllerBase
{
    [HttpPost("greenfield")]
    [ProducesResponseType(typeof(WorkflowStatusResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateGreenfield([FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        var status = await service.CreateGreenfieldAsync(request.Requirement, cancellationToken);
        return CreatedAtAction(nameof(GetStatus), new { workflowId = status.WorkflowId }, status);
    }

    [HttpPost("{workflowId:guid}/advance")]
    [ProducesResponseType(typeof(WorkflowStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowStatusResponse>> Advance(Guid workflowId, CancellationToken cancellationToken) =>
        Ok(await service.AdvanceAsync(workflowId, cancellationToken));

    [HttpGet("{workflowId:guid}")]
    [ProducesResponseType(typeof(WorkflowStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowStatusResponse>> GetStatus(Guid workflowId, CancellationToken cancellationToken) =>
        Ok(await service.GetStatusAsync(workflowId, cancellationToken));
}
