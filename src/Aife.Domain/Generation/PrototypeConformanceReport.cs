using Aife.Domain.Enums;

namespace Aife.Domain.Generation;

/// <summary>
/// The Prototype Conformance Reviewer output: drift findings against the Design
/// System and reference UI (ADR-005). Advisory and non-blocking in the MVP.
/// </summary>
public sealed class PrototypeConformanceReport
{
    public required string PrototypeId { get; init; }
    public ReviewOutcome Outcome { get; init; }
    public IList<ConformanceFinding> Findings { get; init; } = new List<ConformanceFinding>();
    public IList<string> Suggestions { get; init; } = new List<string>();
}
