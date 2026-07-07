# Class Diagrams

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 21 (Class Diagrams)

## Summary

Class diagrams for each bounded context, consistent with the domain model and the
architecture documents. Diagrams stay under roughly 15 elements each and use one
diagram per question. Naming matches the ubiquitous language in
`00-foundation/04-domain-model.md`.

## Generation domain

Audience: developers. Shows the entities a generation session produces and
references.

```mermaid
classDiagram
    class GenerationSession {
        +SessionId
        +PrototypeId
        +Status
        +Stages
    }
    class Prototype {
        +Id
        +Html
        +Css
    }
    class PrototypeAnalysis {
        +Layout
        +Elements
    }
    class DetectedElement {
        +Kind
        +Text
        +Bounds
    }
    class ComponentMapping {
        +ElementRef
        +ComponentId
        +Confidence
    }
    class IntermediateUiTree {
        +Page
        +Layout
        +Children
    }
    class UiNode {
        +ComponentId
        +Props
        +TokenBindings
    }
    class GeneratedArtifact {
        +Path
        +Content
    }
    class ReviewReport {
        +Score
        +Outcome
        +Violations
    }
    GenerationSession --> Prototype
    GenerationSession --> PrototypeAnalysis
    GenerationSession --> IntermediateUiTree
    GenerationSession --> GeneratedArtifact
    GenerationSession --> ReviewReport
    PrototypeAnalysis --> DetectedElement
    IntermediateUiTree --> UiNode
    UiNode --> UiNode : children
```

## Abstractions and implementations

Audience: developers. Shows the interfaces in `Aife.Application` and their
implementations, and the boundary that keeps AI code decoupled from sources and
providers.

```mermaid
classDiagram
    class IPrototypeAnalyzer {
        <<interface>>
        +AnalyzeAsync(Prototype)
    }
    class IComponentMapper {
        <<interface>>
        +MapAsync(PrototypeAnalysis)
    }
    class IReactGenerator {
        <<interface>>
        +GenerateAsync(IntermediateUiTree)
    }
    class IAiReviewer {
        <<interface>>
        +ReviewAsync(Artifacts, Tree)
    }
    class IPrototypeGenerator {
        <<interface>>
        +GenerateAsync(PrototypeRequest)
    }
    class IPrototypeConformanceReviewer {
        <<interface>>
        +ReviewAsync(PrototypeAnalysis, Html, Css)
    }
    class IKnowledgeProvider {
        <<interface>>
        +SearchComponentsAsync(query)
        +GetComponentAsync(id)
        +GetDesignTokensAsync()
    }
    class ILlmProvider {
        <<interface>>
        +CompleteAsync(request)
    }
    class LlmRouter {
        +SelectProvider(request)
        +CompleteAsync(request)
    }
    class JsonKnowledgeProvider
    class McpKnowledgeProvider
    class AzureOpenAiProvider
    class SecondaryProvider
    class PrototypeGenerator
    class PrototypeConformanceReviewer
    IPrototypeGenerator ..> IKnowledgeProvider : uses
    IPrototypeGenerator ..> LlmRouter : uses
    IPrototypeConformanceReviewer ..> IKnowledgeProvider : uses
    IPrototypeConformanceReviewer ..> LlmRouter : uses
    IPrototypeGenerator <|.. PrototypeGenerator
    IPrototypeConformanceReviewer <|.. PrototypeConformanceReviewer
    IComponentMapper ..> IKnowledgeProvider : uses
    IReactGenerator ..> IKnowledgeProvider : uses
    IReactGenerator ..> LlmRouter : uses
    LlmRouter --> ILlmProvider
    IKnowledgeProvider <|.. JsonKnowledgeProvider
    IKnowledgeProvider <|.. McpKnowledgeProvider
    ILlmProvider <|.. AzureOpenAiProvider
    ILlmProvider <|.. SecondaryProvider
```

## Persistence

Audience: developers. Shows the repository interfaces in `Aife.Application` and
their swappable implementations. The API composition root wires the file
implementations in the MVP; Cosmos is the post-MVP target (ADR-004 amendment).

```mermaid
classDiagram
    class ISessionRepository {
        <<interface>>
        +GetAsync(id)
        +SaveAsync(session)
    }
    class IArtifactRepository {
        <<interface>>
        +ListAsync(sessionId)
        +SaveAsync(artifact)
    }
    class IPromptRepository {
        <<interface>>
        +GetAsync(key)
        +SaveAsync(template)
    }
    class IPromptVersionRepository {
        <<interface>>
        +GetAsync(key, version)
        +SaveAsync(version)
    }
    class IConformanceReportRepository {
        <<interface>>
        +GetAsync(prototypeId)
        +SaveAsync(report)
    }
    class FileSessionRepository
    class FileArtifactRepository
    class FilePromptRepository
    class FilePromptVersionRepository
    class FileConformanceReportRepository
    class CosmosSessionRepository
    class CosmosArtifactRepository
    class CosmosPromptRepository
    class CosmosPromptVersionRepository
    class CosmosConformanceReportRepository
    ISessionRepository <|.. FileSessionRepository
    ISessionRepository <|.. CosmosSessionRepository
    IArtifactRepository <|.. FileArtifactRepository
    IArtifactRepository <|.. CosmosArtifactRepository
    IPromptRepository <|.. FilePromptRepository
    IPromptRepository <|.. CosmosPromptRepository
    IPromptVersionRepository <|.. FilePromptVersionRepository
    IPromptVersionRepository <|.. CosmosPromptVersionRepository
    IConformanceReportRepository <|.. FileConformanceReportRepository
    IConformanceReportRepository <|.. CosmosConformanceReportRepository
```

## What is not shown

- Domain invariants and value object detail: see `00-foundation/04-domain-model.md`.
- Request sequences between these classes: see `03-diagrams/02-sequence-diagrams.md`.
