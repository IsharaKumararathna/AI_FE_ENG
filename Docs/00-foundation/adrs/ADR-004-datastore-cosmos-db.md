# ADR-004: Datastore is Azure Cosmos DB

Status: Accepted · Date: 2026-07-07

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
