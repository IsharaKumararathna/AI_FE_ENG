# Sequence Diagrams

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 22 (Sequence Diagrams)

## Summary

Seven sequence diagrams covering the flows that matter most: the full pipeline,
prototype generation from intent, prototype conformance review, a knowledge
lookup, LLM Router provider selection, the AI Review flow, and prompt version
resolution. Diagram names reference every stage and schema by name to support
pipeline traceability.

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

## Prototype generation flow

Audience: developers. Shows a Product Owner generating a DS-conformant prototype
from intent (ADR-006). The output is conformant by construction; it then feeds
the normal pipeline.

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant PG as Prototype Generator
    participant KP as IKnowledgeProvider
    participant R as LlmRouter
    participant Store as Prototype Repo
    Client->>API: POST /prototypes/generate (PrototypeRequest)
    API->>PG: GenerateAsync(PrototypeRequest)
    PG->>KP: SearchComponentsAsync / GetDesignTokensAsync / GetLayoutPatternsAsync
    KP-->>PG: approved components, tokens, layouts, referenceUiPatterns
    PG->>R: CompleteAsync(generation prompt)
    R-->>PG: Prototype (html, css) + IntermediateUiTree
    PG->>PG: validate (approved components, no hardcoded literals)
    PG->>Store: save generated Prototype
    API-->>Client: 202 { prototypeId }, Location: /prototypes/{id}
    Client->>API: GET /prototypes/{id}
    API-->>Client: conformant Prototype (by construction)
```

## Prototype conformance review flow

Audience: developers. Shows the input-side review of an uploaded prototype
(ADR-005). Advisory and non-blocking in the MVP. Triggered on
`GET /prototypes/{id}/conformance`; the report is computed and cached on first
request.

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant AN as Prototype Analyzer
    participant PCR as Prototype Conformance Reviewer
    participant KP as IKnowledgeProvider
    participant R as LlmRouter
    participant Store as Conformance Report Repo
    Client->>API: GET /prototypes/{id}/conformance
    API->>Store: GetAsync(prototypeId)
    alt not cached
        API->>AN: AnalyzeAsync(Prototype)
        AN-->>API: PrototypeAnalysis
        API->>PCR: ReviewAsync(PrototypeAnalysis, html, css)
        PCR->>KP: tokens, layouts, referenceUiPatterns, a11y rules
        KP-->>PCR: knowledge entries
        PCR->>R: CompleteAsync(conformance prompt)
        R-->>PCR: PrototypeConformanceReport JSON
        PCR->>PCR: validate against schema
        PCR->>Store: save report
    end
    API-->>Client: PrototypeConformanceReport (advisory findings)
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
