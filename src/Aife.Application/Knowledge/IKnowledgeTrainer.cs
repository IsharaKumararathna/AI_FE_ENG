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
    /// <param name="folderPath">Path to the source repository root.</param>
    /// <param name="mode">Replace = wipe old components first, Update = merge with existing.</param>
    Task<TrainResult> TrainFromFolderAsync(string folderPath, TrainMode mode, CancellationToken ct);

    /// <summary>
    /// Train from a git repository URL. Clones the repo, then delegates
    /// to <see cref="TrainFromFolderAsync"/>.
    /// </summary>
    Task<TrainResult> TrainFromGitAsync(string gitUrl, string? branch, TrainMode mode, CancellationToken ct);
}

/// <summary>
/// Controls how training interacts with the existing Knowledge Base.
/// </summary>
public enum TrainMode
{
    /// <summary>Merge discovered components with the existing manifest.</summary>
    Update = 0,

    /// <summary>Wipe all existing components, then write only discovered ones.</summary>
    Replace = 1,
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
