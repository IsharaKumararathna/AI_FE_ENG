using Aife.Domain.Generation;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists uploaded or generated prototypes.
/// </summary>
public interface IPrototypeRepository
{
    Task<Prototype?> GetAsync(string id, CancellationToken ct);

    Task SaveAsync(Prototype prototype, CancellationToken ct);
}
