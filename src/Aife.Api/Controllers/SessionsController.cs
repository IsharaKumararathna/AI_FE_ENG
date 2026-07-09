using Aife.Application.Pipeline;
using Aife.Application.Persistence;
using Aife.Api.Models;
using Aife.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aife.Api.Controllers;

[ApiController]
[Route("api/v1/sessions")]
public sealed class SessionsController : ControllerBase
{
    private readonly IPrototypeRepository _prototypeRepository;
    private readonly RunGenerationSessionHandler _handler;
    private readonly ISessionResultStore _resultStore;

    public SessionsController(
        IPrototypeRepository prototypeRepository,
        RunGenerationSessionHandler handler,
        ISessionResultStore resultStore)
    {
        _prototypeRepository = prototypeRepository;
        _handler = handler;
        _resultStore = resultStore;
    }

    [HttpPost]
    public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request, CancellationToken ct)
    {
        var prototype = await _prototypeRepository.GetAsync(request.PrototypeId, ct);
        if (prototype is null)
            return NotFound(new ProblemDetails
            {
                Title = "Prototype not found",
                Status = 404,
                Detail = $"No prototype with id '{request.PrototypeId}'."
            });

        // MVP: runs synchronously (stub LLM is fast). Switch to 202 + background
        // processing when real LLM providers are added.
        var result = await _handler.HandleAsync(prototype, ct);
        _resultStore.Save(result.Session.Id, result);

        Response.Headers["Location"] = $"/api/v1/sessions/{result.Session.Id}";
        return Ok(new { sessionId = result.Session.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetSession(string id, CancellationToken ct)
    {
        var result = _resultStore.Get(id);
        if (result is not null)
            return Ok(result.Session);

        // Fallback: check the session repository (for sessions from a previous run)
        var sessionRepo = HttpContext.RequestServices.GetRequiredService<ISessionRepository>();
        var session = await sessionRepo.GetAsync(id, ct);
        if (session is null)
            return NotFound(new ProblemDetails
            {
                Title = "Session not found",
                Status = 404,
                Detail = $"No session with id '{id}'."
            });

        return Ok(session);
    }

    [HttpGet("{id}/artifacts")]
    public async Task<IActionResult> GetArtifacts(string id, CancellationToken ct)
    {
        var result = _resultStore.Get(id);
        if (result?.Artifacts is not null)
            return Ok(result.Artifacts);

        var artifactRepo = HttpContext.RequestServices.GetRequiredService<IArtifactRepository>();
        var artifacts = await artifactRepo.ListAsync(id, ct);
        if (artifacts.Count == 0)
            return NotFound(new ProblemDetails
            {
                Title = "Artifacts not found",
                Status = 404,
                Detail = $"No artifacts for session '{id}'."
            });

        return Ok(artifacts);
    }

    [HttpGet("{id}/review")]
    public IActionResult GetReview(string id)
    {
        var result = _resultStore.Get(id);
        if (result?.Review is null)
            return NotFound(new ProblemDetails
            {
                Title = "Review report not found",
                Status = 404,
                Detail = $"No review report for session '{id}'."
            });

        return Ok(result.Review);
    }
}
