using Aife.Domain.Generation;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists generated artifacts (React files, review reports) per session.
/// </summary>
public interface IArtifactRepository
{
    Task<IReadOnlyList<GeneratedArtifact>> ListAsync(string sessionId, CancellationToken ct);

    Task SaveAsync(string sessionId, IEnumerable<GeneratedArtifact> artifacts, CancellationToken ct);
}
