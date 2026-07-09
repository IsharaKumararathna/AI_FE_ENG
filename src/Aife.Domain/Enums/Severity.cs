namespace Aife.Domain.Enums;

/// <summary>
/// Severity of a violation or conformance finding.
/// Blocking findings fail the session; advisory findings are reported but do not block.
/// </summary>
public enum Severity
{
    Blocking,
    Advisory
}
