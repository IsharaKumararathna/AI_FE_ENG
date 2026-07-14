# AI-Powered Enterprise Frontend Development Platform — Documentation

Status: Approved · Date: 2026-07-07 · Version: 1.0

This is the design documentation set for the AI-Powered Enterprise Frontend
Development Platform (research initiative, repository key `aife`). It covers
Phase 0 (Foundation) and the detailed design of the Phase 1 MVP (Prototype to
Production React pipeline). Implementation code is deferred to a later milestone;
this set defines the architecture, domain model, schemas, contracts, AI module
designs, prompt templates, and diagrams that a future build phase executes against.

The vision document `AI_Powered_Enterprise_Frontend_Development_Platform_Plan.md`
is the source of truth. This set decomposes its 30 deliverables into verifiable
artifacts, plus extensions added by ADR-005 (Prototype Conformance Review) and
ADR-006 (Prototype Generation).

**Design baseline: Approved v1.0 — 2026-07-07.** All documents below are at this
baseline; ADRs carry their own decision status (Accepted/Amended). Implementation
executes against this frozen baseline; subsequent changes require a new version.

## Reading order

Read foundation first, then schemas and contracts, then AI module designs, then
diagrams, then cross-cutting.

1. Foundation: `00-foundation/01` through `09`, plus the ADRs.
2. Schemas and contracts: `01-schemas-contracts/01` through `05`.
3. AI module designs: `02-ai-modules/01` through `06`.
4. Diagrams: `03-diagrams/01` and `02`.
5. Cross-cutting and operations: `04-cross-cutting/01` through `09`.

## Conventions

- Backend is .NET with C#, Clean Architecture, one class per file.
- JSON serialization uses Newtonsoft.Json (`JsonConvert`), not System.Text.Json.
- Cosmos DB queries filter server-side; no large reads filtered in memory.
- All knowledge access goes through `IKnowledgeProvider`. AI modules never read
  JSON, Markdown, or YAML directly.
- Diagrams use Mermaid in GitHub code blocks. Line breaks inside labels use `<br/>`.
- Issue branches follow `aife-<issue-number>`.

## Document index

| Document | Title | Milestone | Status |
|---|---|---|---|
| `00-foundation/01-system-architecture.md` | System Architecture | A | Approved |
| `00-foundation/02-module-breakdown.md` | Module Breakdown | A | Approved |
| `00-foundation/03-folder-structure.md` | Folder Structure | A | Approved |
| `00-foundation/04-domain-model.md` | Domain Model | A | Approved |
| `00-foundation/05-backend-architecture.md` | Backend Architecture | A | Approved |
| `00-foundation/06-frontend-architecture.md` | Frontend Architecture | A | Approved |
| `00-foundation/07-ai-architecture.md` | AI Architecture | A | Approved |
| `00-foundation/08-mcp-architecture.md` | MCP Architecture | A | Approved |
| `00-foundation/09-phase1-mvp-work-breakdown.md` | Phase 1 MVP Work Breakdown | A | Approved |
| `00-foundation/adrs/ADR-001` to `ADR-007` | Foundational decisions | A | Accepted |
| `01-schemas-contracts/01-component-catalog-schema.md` | Component Catalog Schema | B | Approved |
| `01-schemas-contracts/02-intermediate-ui-schema.md` | Intermediate UI Schema | B | Approved |
| `01-schemas-contracts/03-knowledge-base-schema.md` | Knowledge Base Schema | B | Approved |
| `01-schemas-contracts/04-api-design.md` | API Design | B | Approved |
| `01-schemas-contracts/05-database-design.md` | Database Design | B | Approved |
| `02-ai-modules/01-prompt-templates.md` | Prompt Templates | C | Approved |
| `02-ai-modules/02-prompt-versioning.md` | Prompt Versioning | C | Approved |
| `02-ai-modules/03-component-mapping-strategy.md` | Component Mapping Strategy | C | Approved |
| `02-ai-modules/04-react-generation-strategy.md` | React Generation Strategy | C | Approved |
| `02-ai-modules/05-ai-review-strategy.md` | AI Review Strategy | C | Approved |
| `02-ai-modules/06-prototype-generation-strategy.md` | Prototype Generation Strategy | C | Approved |
| `02-ai-modules/07-handling-unknown-elements.md` | Handling Unknown Elements | C | Approved |
| `02-ai-modules/08-knowledge-trainer.md` | Knowledge Base Trainer | C | Approved |
| `02-ai-modules/09-preview-renderer.md` | Preview Renderer | C | Approved |
| `03-diagrams/01-class-diagrams.md` | Class Diagrams | D | Approved |
| `03-diagrams/02-sequence-diagrams.md` | Sequence Diagrams | D | Approved |
| `04-cross-cutting/01-mcp-server-design.md` | MCP Server Design and Tool Definitions | E | Approved |
| `04-cross-cutting/02-deployment-architecture.md` | Deployment Architecture | E | Approved |
| `04-cross-cutting/03-cicd-strategy.md` | CI/CD Strategy | E | Approved |
| `04-cross-cutting/04-security.md` | Security | E | Approved |
| `04-cross-cutting/05-testing-strategy.md` | Testing Strategy | E | Approved |
| `04-cross-cutting/06-scalability-strategy.md` | Scalability Strategy | E | Approved |
| `04-cross-cutting/07-extension-strategy.md` | Extension Strategy | E | Approved |
| `04-cross-cutting/08-enterprise-adoption.md` | Enterprise Adoption Strategy | E | Approved |
| `04-cross-cutting/09-future-roadmap.md` | Future Roadmap | E | Approved |

## Coverage matrix (vision deliverables)

| Vision deliverable | Document |
|---|---|
| 1. System Architecture | `00-foundation/01-system-architecture.md` |
| 2. Module Breakdown | `00-foundation/02-module-breakdown.md` |
| 3. Folder Structure | `00-foundation/03-folder-structure.md` |
| 4. Backend Architecture | `00-foundation/05-backend-architecture.md` |
| 5. Frontend Architecture | `00-foundation/06-frontend-architecture.md` |
| 6. AI Architecture | `00-foundation/07-ai-architecture.md` |
| 7. MCP Architecture | `00-foundation/08-mcp-architecture.md` |
| 8. Domain Model | `00-foundation/04-domain-model.md` |
| 9. API Design | `01-schemas-contracts/04-api-design.md` |
| 10. Database Design | `01-schemas-contracts/05-database-design.md` |
| 11. Component Catalog Schema | `01-schemas-contracts/01-component-catalog-schema.md` |
| 12. Intermediate UI Schema | `01-schemas-contracts/02-intermediate-ui-schema.md` |
| 13. Knowledge Base Schema | `01-schemas-contracts/03-knowledge-base-schema.md` |
| 14. Prompt Templates | `02-ai-modules/01-prompt-templates.md` |
| 15. Prompt Versioning | `02-ai-modules/02-prompt-versioning.md` |
| 16. Component Mapping Strategy | `02-ai-modules/03-component-mapping-strategy.md` |
| 17. React Generation Strategy | `02-ai-modules/04-react-generation-strategy.md` |
| 18. AI Review Strategy | `02-ai-modules/05-ai-review-strategy.md` |
| 19. MCP Server Design | `04-cross-cutting/01-mcp-server-design.md` |
| 20. MCP Tool Definitions | `04-cross-cutting/01-mcp-server-design.md` (Tool Definitions section) |
| 21. Class Diagrams | `03-diagrams/01-class-diagrams.md` |
| 22. Sequence Diagrams | `03-diagrams/02-sequence-diagrams.md` |
| 23. Deployment Architecture | `04-cross-cutting/02-deployment-architecture.md` |
| 24. CI/CD Strategy | `04-cross-cutting/03-cicd-strategy.md` |
| 25. Security | `04-cross-cutting/04-security.md` |
| 26. Testing Strategy | `04-cross-cutting/05-testing-strategy.md` |
| 27. Scalability Strategy | `04-cross-cutting/06-scalability-strategy.md` |
| 28. Extension Strategy | `04-cross-cutting/07-extension-strategy.md` |
| 29. Enterprise Adoption Strategy | `04-cross-cutting/08-enterprise-adoption.md` |
| 30. Future Roadmap | `04-cross-cutting/09-future-roadmap.md` |
