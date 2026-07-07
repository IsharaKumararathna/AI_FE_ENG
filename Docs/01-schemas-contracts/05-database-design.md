# Database Design

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 10 (Database Design)

## Summary

Persistence uses Azure Cosmos DB (NoSQL API), per ADR-004. Five containers hold
sessions, artifacts, prompt templates, prompt versions, and a knowledge cache.
Large generated files are stored in Blob Storage with a Cosmos reference.
Repositories filter server-side using query parameters and partition keys; no
repository pulls a large set and filters in memory.

## MVP scope

Per the ADR-004 MVP-scope amendment, the MVP does not provision Cosmos DB.
Sessions, artifacts, prompt templates, and prompt versions are persisted as
JSON files on disk by file-based repository implementations
(`FileSessionRepository`, `FileArtifactRepository`, `FilePromptRepository`,
`FilePromptVersionRepository`) behind the same application-layer repository
interfaces. The `knowledgeCache` container is omitted in the MVP. The Cosmos
containers, partition keys, query-side filtering convention, and Blob Storage
reference pattern described below are the post-MVP production target and apply
once `CosmosDb*Repository` implementations are registered in the composition
root.

## Containers

| Container | Partition key | Purpose | Example document |
|---|---|---|---|
| `sessions` | `/sessionId` | One generation session and its stage states. | `GenerationSession` |
| `artifacts` | `/sessionId` | Generated files and review reports per session. | `GeneratedArtifact`, `ReviewReport` |
| `prompts` | `/key` | Prompt template metadata and current version pointer. | `PromptTemplate` |
| `promptVersions` | `/key` | Immutable prompt version snapshots. | `PromptVersion` |
| `knowledgeCache` | `/cacheKey` | Optional cache of knowledge lookups to reduce repeated loads. | cached `KnowledgeEntry` arrays |

## Sample document: session

```json
{
  "sessionId": "s-1234",
  "prototypeId": "p-001",
  "status": "Generating",
  "stages": [
    { "name": "Analyze", "status": "Completed", "finishedAt": "2026-07-07T10:02:00Z" },
    { "name": "Map", "status": "Completed", "finishedAt": "2026-07-07T10:02:20Z" },
    { "name": "Generate", "status": "Running" },
    { "name": "Review", "status": "Pending" }
  ],
  "createdAt": "2026-07-07T10:01:50Z"
}
```

## Query-side filtering

Every read passes filters into the Cosmos query, not into memory. Examples:

- Get a session by id: `SELECT * FROM c WHERE c.sessionId = @sessionId`. The
  partition key is `sessionId`, so this is a point read.
- List artifacts for a session:
  `SELECT * FROM c WHERE c.sessionId = @sessionId AND c.type = @type`.
- Retrieve agent session messages or stage outputs: filter by `sessionId` and
  `stage` in the query. Do not pull all session documents and filter in code.

This follows the team convention and reduces RU consumption and latency.

## Large artifacts

Generated React files and review reports can be large. Store bodies in Blob
Storage and keep a reference in the `artifacts` container:

```json
{
  "sessionId": "s-1234",
  "artifactId": "a-01",
  "type": "GeneratedArtifact",
  "path": "src/components/DataTable.tsx",
  "blobUri": "https://storage/aife/s-1234/a-01",
  "sizeBytes": 1840
}
```

Small artifacts (review summaries, mappings) may be stored inline. The threshold
is set in configuration.

## Prompt store

The `prompts` container holds one document per template key with a
`currentVersion` pointer. The `promptVersions` container holds immutable version
snapshots. Publishing a new version writes to `promptVersions` and updates the
pointer in `prompts`; it never mutates an existing version. See
`02-ai-modules/02-prompt-versioning.md`.

## Knowledge cache

The `knowledgeCache` container is optional. `JsonKnowledgeProvider` reads files
in the MVP, so the cache is primarily for the future `McpKnowledgeProvider` to
avoid repeated remote calls. Cache entries carry a TTL and a source version.

## What is not shown

- RU provisioning and scaling: see `04-cross-cutting/06-scalability-strategy.md`.
- Backup and disaster recovery: see `04-cross-cutting/02-deployment-architecture.md`.
- Schema shapes for artifacts and reports: see the domain model and the AI module designs.
