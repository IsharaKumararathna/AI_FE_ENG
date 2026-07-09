using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// The state of one pipeline stage within a generation session.
/// </summary>
public sealed class StageState
{
    public required string Name { get; init; }
    public StageStatus Status { get; set; } = StageStatus.Pending;
    public DateTime? FinishedAt { get; set; }
}
