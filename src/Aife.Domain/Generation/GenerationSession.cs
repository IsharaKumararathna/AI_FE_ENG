using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// One end-to-end pipeline run and its stage states.
/// </summary>
public sealed class GenerationSession
{
    public required string Id { get; init; }
    public required string PrototypeId { get; init; }
    public SessionStatus Status { get; set; } = SessionStatus.Created;
    public IList<StageState> Stages { get; init; } = new List<StageState>();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
