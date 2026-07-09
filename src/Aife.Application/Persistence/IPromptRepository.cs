using Aife.Domain.Prompting;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists prompt template metadata with a current-version pointer.
/// </summary>
public interface IPromptRepository
{
    Task<PromptTemplate?> GetAsync(string key, CancellationToken ct);

    Task<IReadOnlyList<PromptTemplate>> ListAsync(CancellationToken ct);

    Task SaveAsync(PromptTemplate template, CancellationToken ct);
}
