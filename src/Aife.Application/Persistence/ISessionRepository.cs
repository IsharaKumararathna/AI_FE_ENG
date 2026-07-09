using Aife.Domain.Generation;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists generation sessions. MVP uses file implementations; post-MVP uses
/// Cosmos (ADR-004 amendment). Swap is a DI registration change.
/// </summary>
public interface ISessionRepository
{
    Task<GenerationSession?> GetAsync(string id, CancellationToken ct);

    Task SaveAsync(GenerationSession session, CancellationToken ct);
}
