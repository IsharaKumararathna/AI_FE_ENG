using Aife.Application.Persistence;
using Aife.Domain.Generation;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="IConformanceReportRepository" /> for the MVP (ADR-005).
/// Stores each report as <c>{basePath}/conformance/{prototypeId}.json</c>.
/// </summary>
public sealed class FileConformanceReportRepository : IConformanceReportRepository
{
    private readonly string _basePath;

    public FileConformanceReportRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<PrototypeConformanceReport?> GetAsync(string prototypeId, CancellationToken ct)
    {
        var path = GetPath(prototypeId);
        return Task.FromResult(JsonFileHelper.Read<PrototypeConformanceReport>(path));
    }

    public Task SaveAsync(PrototypeConformanceReport report, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(report.PrototypeId), report);
        return Task.CompletedTask;
    }

    private string GetPath(string prototypeId) =>
        Path.Combine(_basePath, "conformance", $"{Sanitize(prototypeId)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
