# Security

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 25 (Security)

## Summary

Security follows least privilege and defense in depth. Secrets live in Key Vault
and are never in code or logs. The API authenticates with Entra ID and accesses
Azure resources via managed identity. LLM calls go through Azure OpenAI content
filtering. Uploaded prototypes are treated as potentially sensitive and are
isolated and purged on a schedule.

## Secret management

- LLM keys, Cosmos keys, and connection strings are stored in Key Vault.
- The API reads secrets at startup via managed identity; secrets are never logged.
- Secret rotation is handled in Key Vault; the API references the current version
  by URI and reloads on rotation.

## Identity and access

- Callers authenticate with Entra ID bearer tokens. The API validates audience
  and issuer.
- The API runs under a managed identity with scoped roles:
  - Cosmos DB: `Cosmos DB Built-in Data Contributor` on the account.
  - Blob Storage: `Storage Blob Data Contributor` on the artifacts container.
  - Key Vault: `Key Vault Secrets User`.
  - Azure OpenAI: `Cognitive Services OpenAI User`.
- RBAC is reviewed in `04-cross-cutting/02-deployment-architecture.md` and the
  Azure RBAC guidance.

## LLM safety

- Azure OpenAI content filtering is enabled for hate, sexual, violence, and
  self-harm categories, configurable per deployment.
- Prompts and outputs are checked for prompt-injection patterns before use. The
  reviewer treats unexpected instruction-like content in generated code as a
  blocking finding.
- Token and rate limits are enforced at the router to control cost and abuse.

## Data handling

- Uploaded prototypes may contain sensitive content. They are stored in a
  dedicated Blob container with private access and a retention policy that purges
  them after a configured period.
- Generated artifacts are stored per session and accessible only to the session
  owner.
- PII is not logged. Application logs redact prompt and artifact content by
  default.

## Rate limiting and abuse

- The API enforces rate limits per caller and per session.
- Long-running generation is queued, so a single caller cannot exhaust LLM
  concurrency. See `04-cross-cutting/06-scalability-strategy.md`.

## What is not shown

- Deployment network controls: see `04-cross-cutting/02-deployment-architecture.md`.
- Reliability and recovery: see `04-cross-cutting/06-scalability-strategy.md`.
