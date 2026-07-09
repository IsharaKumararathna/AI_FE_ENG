using Aife.Application.Persistence;
using Aife.Domain.Prompting;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="IPromptVersionRepository" /> for the MVP (ADR-004 amendment).
/// Stores each version as <c>{basePath}/prompt-versions/{key}/{version}.json</c>.
/// Existing versions are never mutated.
/// </summary>
public sealed class FilePromptVersionRepository : IPromptVersionRepository
{
    private readonly string _basePath;

    public FilePromptVersionRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<PromptVersion?> GetAsync(string key, string version, CancellationToken ct)
    {
        var path = GetPath(key, version);
        return Task.FromResult(JsonFileHelper.Read<PromptVersion>(path));
    }

    public Task<IReadOnlyList<PromptVersion>> ListAsync(string key, CancellationToken ct)
    {
        var dir = Path.Combine(_basePath, "prompt-versions", Sanitize(key));
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<PromptVersion>>(new List<PromptVersion>());

        var versions = new List<PromptVersion>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var v = JsonFileHelper.Read<PromptVersion>(file);
            if (v is not null)
                versions.Add(v);
        }

        return Task.FromResult<IReadOnlyList<PromptVersion>>(versions);
    }

    public Task SaveAsync(PromptVersion version, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(version.Key, version.Version), version);
        return Task.CompletedTask;
    }

    private string GetPath(string key, string version) =>
        Path.Combine(_basePath, "prompt-versions", Sanitize(key), $"{Sanitize(version)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
