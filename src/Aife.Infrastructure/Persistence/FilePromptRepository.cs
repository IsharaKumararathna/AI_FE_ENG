using Aife.Application.Persistence;
using Aife.Domain.Prompting;

namespace Aife.Infrastructure.Persistence;

/// <summary>
/// File-based <see cref="IPromptRepository" /> for the MVP (ADR-004 amendment).
/// Stores each template as <c>{basePath}/prompts/{key}.json</c>.
/// </summary>
public sealed class FilePromptRepository : IPromptRepository
{
    private readonly string _basePath;

    public FilePromptRepository(string basePath)
    {
        _basePath = basePath;
    }

    public Task<PromptTemplate?> GetAsync(string key, CancellationToken ct)
    {
        var path = GetPath(key);
        return Task.FromResult(JsonFileHelper.Read<PromptTemplate>(path));
    }

    public Task<IReadOnlyList<PromptTemplate>> ListAsync(CancellationToken ct)
    {
        var dir = Path.Combine(_basePath, "prompts");
        if (!Directory.Exists(dir))
            return Task.FromResult<IReadOnlyList<PromptTemplate>>(new List<PromptTemplate>());

        var templates = new List<PromptTemplate>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var template = JsonFileHelper.Read<PromptTemplate>(file);
            if (template is not null)
                templates.Add(template);
        }

        return Task.FromResult<IReadOnlyList<PromptTemplate>>(templates);
    }

    public Task SaveAsync(PromptTemplate template, CancellationToken ct)
    {
        JsonFileHelper.Write(GetPath(template.Key), template);
        return Task.CompletedTask;
    }

    private string GetPath(string key) =>
        Path.Combine(_basePath, "prompts", $"{Sanitize(key)}.json");

    private static string Sanitize(string id) =>
        id.Replace("..", "").Replace(Path.DirectorySeparatorChar, '_');
}
