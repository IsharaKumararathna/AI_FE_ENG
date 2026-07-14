namespace Aife.Application.Knowledge;

/// <summary>
/// Trains the Knowledge Base from a source code repository or folder.
/// Scans SCSS variable files for design tokens, and React/TSX component
/// files for props, categories, and descriptions.
/// </summary>
public interface IKnowledgeTrainer
{
    /// <summary>
    /// Train from a local folder path. Scans for Variables.scss (tokens)
    /// and CustomUI component files (components).
    /// </summary>
    Task<TrainResult> TrainFromFolderAsync(string folderPath, CancellationToken ct);

    /// <summary>
    /// Train from a git repository URL. Clones the repo, then delegates
    /// to <see cref="TrainFromFolderAsync"/>.
    /// </summary>
    Task<TrainResult> TrainFromGitAsync(string gitUrl, string? branch, CancellationToken ct);
}

public sealed record TrainResult
{
    public bool Success { get; init; }
    public int TokensExtracted { get; init; }
    public int ComponentsExtracted { get; init; }
    public IList<string> Warnings { get; init; } = new List<string>();
    public IList<string> Errors { get; init; } = new List<string>();
    public string? KnowledgeBasePath { get; init; }
}
