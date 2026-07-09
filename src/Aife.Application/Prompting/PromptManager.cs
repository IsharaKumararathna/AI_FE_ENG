using Aife.Application.Persistence;

namespace Aife.Application.Prompting;

/// <summary>
/// Default <see cref="IPromptManager" />. Resolves the current version from the
/// prompt store and fills <c>{{variables}}</c> in the system message.
/// </summary>
public sealed class PromptManager : IPromptManager
{
    private readonly IPromptRepository _promptRepository;
    private readonly IPromptVersionRepository _versionRepository;

    public PromptManager(IPromptRepository promptRepository, IPromptVersionRepository versionRepository)
    {
        _promptRepository = promptRepository;
        _versionRepository = versionRepository;
    }

    public async Task<AssembledPrompt> GetPromptAsync(
        string key,
        IDictionary<string, string> variables,
        CancellationToken ct)
    {
        var template = await _promptRepository.GetAsync(key, ct)
            ?? throw new InvalidOperationException($"No prompt template found for key: {key}");

        if (string.IsNullOrEmpty(template.CurrentVersion))
            throw new InvalidOperationException($"Prompt template '{key}' has no current version.");

        var version = await _versionRepository.GetAsync(key, template.CurrentVersion, ct)
            ?? throw new InvalidOperationException(
                $"No prompt version found for key: {key}, version: {template.CurrentVersion}");

        var filledMessage = FillVariables(version.SystemMessage, variables);

        return new AssembledPrompt
        {
            Key = key,
            Version = version.Version,
            SystemMessage = filledMessage,
            Content = filledMessage,
            OutputContract = version.OutputContract
        };
    }

    private static string FillVariables(string template, IDictionary<string, string> variables)
    {
        var result = template;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{{{key}}}}}", value);
        }

        return result;
    }
}
