using Aife.Domain.Generation;

namespace Aife.Application.Persistence;

/// <summary>
/// Persists prototype conformance reports (ADR-005). Advisory in the MVP.
/// </summary>
public interface IConformanceReportRepository
{
    Task<PrototypeConformanceReport?> GetAsync(string prototypeId, CancellationToken ct);

    Task SaveAsync(PrototypeConformanceReport report, CancellationToken ct);
}
