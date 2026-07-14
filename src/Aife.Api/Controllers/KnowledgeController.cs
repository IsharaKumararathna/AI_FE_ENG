using Aife.Application.Knowledge;
using Aife.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace Aife.Api.Controllers;

[ApiController]
[Route("api/v1/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeTrainer _trainer;

    public KnowledgeController(IKnowledgeTrainer trainer)
    {
        _trainer = trainer;
    }

    /// <summary>
    /// Train/update the Knowledge Base from a local folder path or git URL.
    /// </summary>
    [HttpPost("train")]
    public async Task<IActionResult> Train([FromBody] TrainRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FolderPath) && string.IsNullOrWhiteSpace(request.GitUrl))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing source",
                Status = 400,
                Detail = "Provide either folderPath (local directory) or gitUrl (repository URL)."
            });
        }

        TrainResult result;

        if (!string.IsNullOrWhiteSpace(request.GitUrl))
        {
            result = await _trainer.TrainFromGitAsync(request.GitUrl, request.GitBranch, ct);
        }
        else
        {
            if (!Directory.Exists(request.FolderPath))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Folder not found",
                    Status = 400,
                    Detail = $"The folder '{request.FolderPath}' does not exist."
                });
            }

            result = await _trainer.TrainFromFolderAsync(request.FolderPath!, ct);
        }

        if (!result.Success)
        {
            return StatusCode(500, new ProblemDetails
            {
                Title = "Training failed",
                Status = 500,
                Detail = string.Join("; ", result.Errors)
            });
        }

        // Force the singleton provider to reload from disk after training
        if (HttpContext.RequestServices.GetRequiredService<IKnowledgeProvider>() is JsonKnowledgeProvider jsonProvider)
        {
            jsonProvider.Reload();
        }

        return Ok(new
        {
            result.Success,
            result.TokensExtracted,
            result.ComponentsExtracted,
            result.Warnings,
            result.KnowledgeBasePath
        });
    }

    /// <summary>
    /// Get the current Knowledge Base summary.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var provider = HttpContext.RequestServices.GetRequiredService<IKnowledgeProvider>();
        var components = await provider.SearchComponentsAsync(new ComponentQuery(), ct);
        var tokens = await provider.GetDesignTokensAsync(ct);

        return Ok(new
        {
            componentCount = components.Count,
            tokenCount = tokens.Tokens.Count,
            components = components.Select(c => new { c.ComponentId, c.Name, c.Category }).ToList(),
            tokenCategories = tokens.Tokens.GroupBy(t => t.Category)
                .ToDictionary(g => g.Key, g => g.Count())
        });
    }
}

public sealed class TrainRequest
{
    public string? FolderPath { get; init; }
    public string? GitUrl { get; init; }
    public string? GitBranch { get; init; }
}
