namespace Aife.Domain.Enums;

/// <summary>
/// Lifecycle state of a generation session.
/// </summary>
public enum SessionStatus
{
    Created,
    Analyzing,
    Mapping,
    Generating,
    Reviewing,
    Completed,
    Failed
}
