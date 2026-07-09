using Aife.Application.Persistence;
using Aife.Domain.Generation;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="IArtifactRepository" /> for the MVP (ADR-004 amendment).
/// Stores artifacts per session as <c>{basePath}/artifacts/{sessionId}.json</c>.
/// </summary>
public sealed class FileArtifactRepository : IArtifactRepository
{
    private readonly string _basePath;

    public FileArtifactRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<IReadOnlyList<GeneratedArtifact>> ListAsync(string sessionId, CancellationToken ct)
    {
        var path = GetPath(sessionId);
        var artifacts = JsonFileHelper.Read<List<GeneratedArtifact>>(path) ?? new List<GeneratedArtifact>();
        return Task.FromResult<IReadOnlyList<GeneratedArtifact>>(artifacts);
    }

    public Task SaveAsync(string sessionId, IEnumerable<GeneratedArtifact> artifacts, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(sessionId), artifacts.ToList());
        return Task.CompletedTask;
    }

    private string GetPath(string sessionId) =>
        Path.Combine(_basePath, "artifacts", $"{Sanitize(sessionId)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
