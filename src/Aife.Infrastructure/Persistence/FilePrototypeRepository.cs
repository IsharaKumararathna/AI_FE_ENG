using Aife.Application.Persistence;
using Aife.Domain.Generation;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="IPrototypeRepository" /> for the MVP (ADR-004 amendment).
/// Stores each prototype as <c>{basePath}/prototypes/{id}.json</c>.
/// </summary>
public sealed class FilePrototypeRepository : IPrototypeRepository
{
    private readonly string _basePath;

    public FilePrototypeRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<Prototype?> GetAsync(string id, CancellationToken ct)
    {
        var path = GetPath(id);
        return Task.FromResult(JsonFileHelper.Read<Prototype>(path));
    }

    public Task SaveAsync(Prototype prototype, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(prototype.Id), prototype);
        return Task.CompletedTask;
    }

    private string GetPath(string id) =>
        Path.Combine(_basePath, "prototypes", $"{Sanitize(id)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
