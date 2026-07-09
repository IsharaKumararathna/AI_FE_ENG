using Aife.Application.AI.Stages;
using Aife.Application.Persistence;
using Aife.Domain.Generation;
using Aife.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aife.Api.Controllers;

[ApiController]
[Route("api/v1/prototypes")]
public sealed class PrototypesController : ControllerBase
{
    private readonly IPrototypeRepository _prototypeRepository;
    private readonly IPrototypeAnalyzer _analyzer;
    private readonly IPrototypeConformanceReviewer _conformanceReviewer;
    private readonly IConformanceReportRepository _conformanceRepo;
    private readonly IPrototypeGenerator _generator;

    public PrototypesController(
        IPrototypeRepository prototypeRepository,
        IPrototypeAnalyzer analyzer,
        IPrototypeConformanceReviewer conformanceReviewer,
        IConformanceReportRepository conformanceRepo,
        IPrototypeGenerator generator)
    {
        _prototypeRepository = prototypeRepository;
        _analyzer = analyzer;
        _conformanceReviewer = conformanceReviewer;
        _conformanceRepo = conformanceRepo;
        _generator = generator;
    }

    [HttpPost]
    public async Task<IActionResult> Upload([FromBody] UploadPrototypeRequest request, CancellationToken ct)
    {
        var prototype = new Prototype
        {
            Id = $"p-{Guid.NewGuid():N}",
            Html = request.Html,
            Css = request.Css
        };

        await _prototypeRepository.SaveAsync(prototype, ct);

        return CreatedAtAction(nameof(Get), new { id = prototype.Id }, prototype);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] PrototypeRequest request, CancellationToken ct)
    {
        var result = await _generator.GenerateAsync(request, ct);
        await _prototypeRepository.SaveAsync(result.Prototype, ct);

        return Ok(new { prototypeId = result.Prototype.Id });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var prototype = await _prototypeRepository.GetAsync(id, ct);
        if (prototype is null)
            return NotFound(new ProblemDetails { Title = "Prototype not found", Status = 404, Detail = $"No prototype with id '{id}'." });

        return Ok(prototype);
    }

    [HttpGet("{id}/conformance")]
    public async Task<IActionResult> GetConformance(string id, CancellationToken ct)
    {
        var prototype = await _prototypeRepository.GetAsync(id, ct);
        if (prototype is null)
            return NotFound(new ProblemDetails
            {
                Title = "Prototype not found",
                Status = 404,
                Detail = $"No prototype with id '{id}'."
            });

        // Check cache (ADR-005: report is computed and cached on first request)
        var cached = await _conformanceRepo.GetAsync(id, ct);
        if (cached is not null)
            return Ok(cached);

        // Compute: analyze + review
        var analysis = await _analyzer.AnalyzeAsync(prototype, ct);
        var report = await _conformanceReviewer.ReviewAsync(prototype, analysis, ct);
        await _conformanceRepo.SaveAsync(report, ct);

        return Ok(report);
    }
}
