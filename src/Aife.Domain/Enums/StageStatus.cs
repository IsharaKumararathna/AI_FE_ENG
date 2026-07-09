namespace Aife.Domain.Enums;

/// <summary>
/// Execution state of a single pipeline stage within a session.
/// </summary>
public enum StageStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
