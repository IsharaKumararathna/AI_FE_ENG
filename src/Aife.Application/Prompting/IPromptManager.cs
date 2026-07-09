namespace Aife.Application.Prompting;

/// <summary>
/// Assembles prompts from versioned templates. AI stages call the Prompt
/// Manager, never the prompt store directly. The manager resolves the current
/// version for a key and fills <c>{{variables}}</c>.
/// </summary>
public interface IPromptManager
{
    Task<AssembledPrompt> GetPromptAsync(
        string key,
        IDictionary<string, string> variables,
        CancellationToken ct);
}
