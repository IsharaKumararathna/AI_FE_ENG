using Aife.Application.Knowledge;
using Aife.Domain.Generation;

namespace Aife.Application.AI.Stages;

/// <summary>
/// Default <see cref="IUiTreeAssembler" />. Places auto-applied mappings (confidence
/// ≥ 0.5) into the tree and resolves token bindings from the Knowledge Base.
/// </summary>
public sealed class UiTreeAssembler : IUiTreeAssembler
{
    private readonly IKnowledgeProvider _knowledgeProvider;

    public UiTreeAssembler(IKnowledgeProvider knowledgeProvider)
    {
        _knowledgeProvider = knowledgeProvider;
    }

    public async Task<IntermediateUiTree> AssembleAsync(
        PrototypeAnalysis analysis,
        IReadOnlyList<ComponentMapping> mappings,
        CancellationToken ct)
    {
        var tree = new IntermediateUiTree
        {
            Page = "Generated",
            Layout = analysis.Layout,
            Children = new List<UiNode>()
        };

        foreach (var mapping in mappings.Where(m => m.Confidence >= 0.5 && m.ComponentId is not null))
        {
            var component = await _knowledgeProvider.GetComponentAsync(mapping.ComponentId!, ct);
            var tokenBindings = new Dictionary<string, string>();

            if (component?.TokensConsumed is not null)
            {
                foreach (var token in component.TokensConsumed)
                {
                    tokenBindings[token] = token;
                }
            }

            tree.Children.Add(new UiNode
            {
                NodeId = $"n-{tree.Children.Count + 1}",
                ComponentId = mapping.ComponentId!,
                TokenBindings = tokenBindings
            });
        }

        return tree;
    }
}
