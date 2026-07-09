using Aife.Application.Pipeline;

namespace Aife.Api.Services;

/// <summary>
/// Caches the full generation result per session so GET endpoints can return
/// analysis, mappings, tree, artifacts, and review after the pipeline completes.
/// In-memory for the MVP; replace with a persistent store for production.
/// </summary>
public interface ISessionResultStore
{
    void Save(string sessionId, GenerationResult result);

    GenerationResult? Get(string sessionId);
}
