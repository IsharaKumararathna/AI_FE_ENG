# Class Diagrams

Status: Draft · Date: 2026-07-07 · Version: 0.1
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
    IComponentMapper ..> IKnowledgeProvider : uses
    IReactGenerator ..> IKnowledgeProvider : uses
    IReactGenerator ..> LlmRouter : uses
    LlmRouter --> ILlmProvider
    IKnowledgeProvider <|.. JsonKnowledgeProvider
    IKnowledgeProvider <|.. McpKnowledgeProvider
    ILlmProvider <|.. AzureOpenAiProvider
    ILlmProvider <|.. SecondaryProvider
```

## What is not shown

- Domain invariants and value object detail: see `00-foundation/04-domain-model.md`.
- Request sequences between these classes: see `03-diagrams/02-sequence-diagrams.md`.
