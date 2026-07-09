using Aife.Domain.Prompting;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists immutable prompt version snapshots. Publishing a new version writes
/// a new snapshot and never mutates an existing one.
/// </summary>
public interface IPromptVersionRepository
{
    Task<PromptVersion?> GetAsync(string key, string version, CancellationToken ct);

    Task<IReadOnlyList<PromptVersion>> ListAsync(string key, CancellationToken ct);

    Task SaveAsync(PromptVersion version, CancellationToken ct);
}
