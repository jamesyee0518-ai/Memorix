using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseController : ControllerBase
{
    protected string? GetTraceId()
    {
        return HttpContext.Items.TryGetValue("trace_id", out var traceId) ? traceId?.ToString() : null;
    }
}
