using Aife.Application.AI.Stages;
using Aife.Application.Persistence;
using Aife.Domain.Enums;
using Aife.Domain.Generation;

namespace Aife.Application.Pipeline;

/// <summary>
/// Orchestrates one generation session end to end: Analyze → Map → Assemble →
/// Generate → Review. Persists the session and artifacts through repositories.
/// Stages are addressed by interface, so they can be replaced or reordered.
/// </summary>
public sealed class RunGenerationSessionHandler
{
    private readonly IPrototypeAnalyzer _analyzer;
    private readonly IComponentMapper _mapper;
    private readonly IUiTreeAssembler _assembler;
    private readonly IReactGenerator _generator;
    private readonly IAiReviewer _reviewer;
    private readonly ISessionRepository _sessionRepository;
    private readonly IArtifactRepository _artifactRepository;

    public RunGenerationSessionHandler(
        IPrototypeAnalyzer analyzer,
        IComponentMapper mapper,
        IUiTreeAssembler assembler,
        IReactGenerator generator,
        IAiReviewer reviewer,
        ISessionRepository sessionRepository,
        IArtifactRepository artifactRepository)
    {
        _analyzer = analyzer;
        _mapper = mapper;
        _assembler = assembler;
        _generator = generator;
        _reviewer = reviewer;
        _sessionRepository = sessionRepository;
        _artifactRepository = artifactRepository;
    }

    public async Task<GenerationResult> HandleAsync(Prototype prototype, CancellationToken ct)
    {
        var sessionId = $"s-{Guid.NewGuid():N}";
        var session = new GenerationSession
        {
            Id = sessionId,
            PrototypeId = prototype.Id,
            Status = SessionStatus.Analyzing,
            Stages = new List<StageState>
            {
                new() { Name = "Analyze" },
                new() { Name = "Map" },
                new() { Name = "Generate" },
                new() { Name = "Review" }
            }
        };

        await _sessionRepository.SaveAsync(session, ct);

        // Analyze
        SetStage(session, "Analyze", StageStatus.Running);
        await _sessionRepository.SaveAsync(session, ct);

        var analysis = await _analyzer.AnalyzeAsync(prototype, ct);
        SetStage(session, "Analyze", StageStatus.Completed);

        // Map
        session.Status = SessionStatus.Mapping;
        SetStage(session, "Map", StageStatus.Running);
        await _sessionRepository.SaveAsync(session, ct);

        var mappings = await _mapper.MapAsync(analysis, ct);
        SetStage(session, "Map", StageStatus.Completed);

        // Assemble
        var tree = await _assembler.AssembleAsync(analysis, mappings, ct);

        // Generate
        session.Status = SessionStatus.Generating;
        SetStage(session, "Generate", StageStatus.Running);
        await _sessionRepository.SaveAsync(session, ct);

        var artifacts = await _generator.GenerateAsync(tree, ct);
        await _artifactRepository.SaveAsync(sessionId, artifacts, ct);
        SetStage(session, "Generate", StageStatus.Completed);

        // Review
        session.Status = SessionStatus.Reviewing;
        SetStage(session, "Review", StageStatus.Running);
        await _sessionRepository.SaveAsync(session, ct);

        var review = await _reviewer.ReviewAsync(artifacts, tree, ct);
        SetStage(session, "Review", StageStatus.Completed);

        // Complete
        session.Status = SessionStatus.Completed;
        await _sessionRepository.SaveAsync(session, ct);

        return new GenerationResult
        {
            Session = session,
            Analysis = analysis,
            Mappings = mappings,
            Tree = tree,
            Artifacts = artifacts,
            Review = review
        };
    }

    private static void SetStage(GenerationSession session, string stageName, StageStatus status)
    {
        var stage = session.Stages.FirstOrDefault(s => s.Name == stageName);
        if (stage is null)
            return;

        stage.Status = status;
        if (status is StageStatus.Completed or StageStatus.Failed)
            stage.FinishedAt = DateTime.UtcNow;
    }
}
