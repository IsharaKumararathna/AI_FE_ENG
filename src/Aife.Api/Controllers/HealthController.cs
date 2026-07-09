using Microsoft.AspNetCore.Mvc;

namespace Aife.Api.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Health() => Ok(new { status = "healthy" });
}
