using Aife.Application.Persistence;
using Aife.Domain.Generation;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="ISessionRepository" /> for the MVP (ADR-004 amendment).
/// Stores each session as <c>{basePath}/sessions/{id}.json</c>.
/// </summary>
public sealed class FileSessionRepository : ISessionRepository
{
    private readonly string _basePath;

    public FileSessionRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<GenerationSession?> GetAsync(string id, CancellationToken ct)
    {
        var path = GetPath(id);
        return Task.FromResult(JsonFileHelper.Read<GenerationSession>(path));
    }

    public Task SaveAsync(GenerationSession session, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(session.Id), session);
        return Task.CompletedTask;
    }

    private string GetPath(string id) =>
        Path.Combine(_basePath, "sessions", $"{Sanitize(id)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
