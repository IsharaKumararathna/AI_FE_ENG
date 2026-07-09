using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Assembles mappings into an Intermediate UI Tree. Pure application-layer
/// logic — no LLM calls. Places mapped components into layout slots and
/// resolves token bindings from the Knowledge Base.
/// </summary>
public interface IUiTreeAssembler
{
    Task<IntermediateUiTree> AssembleAsync(
        PrototypeAnalysis analysis,
        IReadOnlyList<ComponentMapping> mappings,
        CancellationToken ct);
}
