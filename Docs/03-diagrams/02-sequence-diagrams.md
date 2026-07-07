# Sequence Diagrams

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 22 (Sequence Diagrams)

## Summary

Five sequence diagrams covering the flows that matter most: the full pipeline, a
knowledge lookup, LLM Router provider selection, the AI Review flow, and prompt
version resolution. Diagram names reference every stage and schema by name to
support pipeline traceability.

## Full pipeline

Audience: developers. Shows one generation session end to end, from prototype
upload to production React plus review report.

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant Orch as RunGenerationSessionHandler
    participant AN as Prototype Analyzer
    participant MP as Component Mapper
    participant Asm as UI Tree Assembler
    participant RG as React Generator
    participant RV as AI Reviewer
    participant Store as Session and Artifact Repos
    Client->>API: POST /prototypes (html, css)
    API->>Store: save Prototype
    Client->>API: POST /sessions (prototypeId)
    API->>Orch: run session
    Orch->>AN: AnalyzeAsync(Prototype)
    AN-->>Orch: PrototypeAnalysis
    Orch->>MP: MapAsync(PrototypeAnalysis)
    MP-->>Orch: ComponentMapping[]
    Orch->>Asm: assemble(tree) from mappings
    Asm-->>Orch: IntermediateUiTree
    Orch->>RG: GenerateAsync(IntermediateUiTree)
    RG-->>Orch: GeneratedArtifact[]
    Orch->>RV: ReviewAsync(Artifacts, Tree)
    RV-->>Orch: ReviewReport
    Orch->>Store: persist analysis, tree, artifacts, report
    Client->>API: GET /sessions/{id}
    API-->>Client: status, artifacts, review
```

## Knowledge lookup

Audience: developers. Shows an AI stage querying knowledge through
`IKnowledgeProvider`, never reading files directly.

```mermaid
sequenceDiagram
    participant Stage as AI Stage
    participant KP as IKnowledgeProvider
    participant JKP as JsonKnowledgeProvider
    participant FS as knowledge/ files
    Stage->>KP: SearchComponentsAsync(query)
    KP->>JKP: dispatch
    JKP->>FS: read (cached after first load)
    FS-->>JKP: raw entries
    JKP-->>KP: ComponentSummary list
    KP-->>Stage: ComponentSummary list
```

## LLM Router provider selection

Audience: developers. Shows the router choosing a provider by capability and
policy, then calling it.

```mermaid
sequenceDiagram
    participant Stage as AI Stage
    participant R as LlmRouter
    participant Reg as Provider Registry
    participant Prov as Selected ILlmProvider
    Stage->>R: CompleteAsync(request, stage context)
    R->>Reg: match capabilities and policy
    Reg-->>R: selected provider
    R->>Prov: CompleteAsync(request)
    Prov-->>R: LlmResponse (text, usage, finish reason)
    R-->>Stage: LlmResponse
```

## AI Review flow

Audience: developers. Shows the reviewer gathering rules and approved components
before scoring.

```mermaid
sequenceDiagram
    participant Orch as Orchestrator
    participant RV as AI Reviewer
    participant KP as IKnowledgeProvider
    participant R as LlmRouter
    participant Store as Artifact Repo
    Orch->>RV: ReviewAsync(Artifacts, Tree)
    RV->>KP: GetAccessibilityRulesAsync()
    KP-->>RV: AccessibilityRule[]
    RV->>KP: SearchComponentsAsync(query)
    KP-->>RV: approved components
    RV->>R: CompleteAsync(review prompt, artifacts)
    R-->>RV: ReviewReport JSON
    RV->>RV: validate against ReviewReport schema
    RV-->>Orch: ReviewReport (score, violations)
    RV->>Store: persist report
```

## Prompt version resolution

Audience: developers. Shows the Prompt Manager resolving the active version and
filling variables before a stage calls the router.

```mermaid
sequenceDiagram
    participant Stage as AI Stage
    participant PM as Prompt Manager
    participant PVM as Prompt Version Manager
    participant DB as Prompt Store
    Stage->>PM: GetPrompt(key, variables)
    PM->>PVM: resolve current version for key
    PVM->>DB: read prompts and promptVersions
    DB-->>PVM: PromptVersion
    PVM-->>PM: PromptVersion
    PM->>PM: fill variables
    PM-->>Stage: assembled prompt plus metadata
```

## What is not shown

- Class structure behind these participants: see `03-diagrams/01-class-diagrams.md`.
- API endpoint details: see `01-schemas-contracts/04-api-design.md`.
