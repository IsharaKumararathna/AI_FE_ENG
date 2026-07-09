namespace Aife.Application.AI;

/// <summary>
/// Context about the calling stage, used by the router to match capabilities.
/// </summary>
public sealed record StageContext
{
    public required string StageName { get; init; }
    public IList<string> RequiredCapabilities { get; init; } = new List<string>();
}
