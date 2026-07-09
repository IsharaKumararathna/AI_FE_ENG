namespace Aife.Domain.Enums;

/// <summary>
/// Approval status of a Design System component.
/// Only <see cref="Approved" /> components are used by the generator.
/// </summary>
public enum ComponentStatus
{
    Approved,
    Deprecated,
    Experimental
}
