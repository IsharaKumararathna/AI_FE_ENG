using System.Collections.Concurrent;
using Aife.Application.Pipeline;

namespace Aife.Api.Services;

public sealed class InMemorySessionResultStore : ISessionResultStore
{
    private readonly ConcurrentDictionary<string, GenerationResult> _store = new();

    public void Save(string sessionId, GenerationResult result) => _store[sessionId] = result;

    public GenerationResult? Get(string sessionId) =>
        _store.TryGetValue(sessionId, out var result) ? result : null;
}
