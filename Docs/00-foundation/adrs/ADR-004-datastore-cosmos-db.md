# ADR-004: Datastore is Azure Cosmos DB

Status: Amended · Date: 2026-07-07 · Amended: 2026-07-07 (MVP scope)

## Context

The platform persists generation sessions, generated artifacts, prompt templates,
prompt versions, and a knowledge cache. Workloads are read-heavy with occasional
large writes (generated code, review reports) and need horizontal scale. The team
already operates Cosmos DB and has a convention to filter in the query rather
than in memory.

## Options considered

- Azure Cosmos DB. Matches team skills and conventions, scales horizontally,
  flexible schema suits evolving AI artifacts.
- Azure SQL Database. Strong relational consistency, but AI artifacts and prompt
  versions are document-shaped and change often.
- PostgreSQL. Capable and portable, but adds a new operational surface for the
  team.

## Decision

Use Azure Cosmos DB (NoSQL API). Containers: `sessions`, `artifacts`, `prompts`,
`promptVersions`, `knowledgeCache`. Partition keys and sample documents are
defined in `01-schemas-contracts/05-database-design.md`.

## Consequences

- Repositories filter server-side using Cosmos query parameters; no large reads
  filtered in memory, per team convention.
- Partition key choice is critical for session and artifact throughput and is
  specified per container in the database design.
- RU provisioning and caching strategy are covered in
  `04-cross-cutting/06-scalability-strategy.md`.
- Large generated artifacts may be stored in Blob Storage with a Cosmos
  reference, decided in the database design.

## Amendment — MVP scope (2026-07-07)

Cosmos DB remains the production datastore target. For the MVP, persistence is
provided by file-system and in-memory repository implementations behind the
same application-layer repository interfaces, so no cloud resources are
required to build and evaluate the initial MVP. This mirrors the precedent set
by ADR-003, where `IKnowledgeProvider` ships a file-based
`JsonKnowledgeProvider` in the MVP and defers `McpKnowledgeProvider`.

### MVP repository strategy

- Define repository interfaces in the application layer: `ISessionRepository`,
  `IArtifactRepository`, `IPromptRepository`, `IPromptVersionRepository`.
- Ship MVP implementations that require no cloud resources:
  `FileSessionRepository`, `FileArtifactRepository`,
  `FilePromptRepository`, `FilePromptVersionRepository` persist JSON files on
  disk, following the `knowledge/` pattern used by `JsonKnowledgeProvider`.
- The `knowledgeCache` container is omitted in the MVP; it was already
  optional in `01-schemas-contracts/05-database-design.md`.
- `CosmosDb*Repository` implementations remain the post-MVP target and are
  registered in the API composition root once Cosmos is provisioned. Swapping
  implementations is a DI registration change only; domain and application
  layers are unaffected.

### Consequences of the amendment

- The MVP stores sessions, artifacts, prompt templates, and prompt versions as
  files on disk. There is no server-side query filtering or partitioning; the
  query-side filtering convention in `05-database-design.md` applies once
  Cosmos repositories are introduced.
- Concurrency, durability, and scale guarantees are not provided by the MVP
  file repositories. This is acceptable for prototype evaluation and local
  development, not for production.
- Large generated artifacts are written to the local file system in the MVP;
  Blob Storage with a Cosmos reference remains the post-MVP target.
- No change to the domain or application layers. The dependency rule and
  Clean Architecture composition-root wiring are preserved.
