# Scalability Strategy

Status: Draft · Date: 2026-07-07 · Version: 0.1
Vision deliverable: 27 (Scalability Strategy)

## Summary

The API is stateless and scales horizontally. Generation work is queued so LLM
concurrency is controlled. Cosmos DB is partitioned for session and artifact
throughput, with RU autoscale. Knowledge lookups are cached to avoid repeated
loads. The LLM Router enforces per-provider concurrency and rate limits.

## Stateless API

- The API holds no session state in memory. All session state is in Cosmos.
- Container Apps scales by HTTP concurrency and CPU. Adding instances does not
  lose work because generation is queued.

## Asynchronous generation queue

- A create-session request enqueues a generation job and returns `202 Accepted`.
- A worker consumes the queue and runs the pipeline. This decouples request
  volume from LLM concurrency.
- The queue may be Azure Service Bus or Storage Queues. The session status in
  Cosmos remains the source of truth for clients polling `GET /sessions/{id}`.

## Cosmos DB scaling

- Partition keys are chosen for even distribution: `sessionId` for sessions and
  artifacts, `key` for prompts and prompt versions. See
  `01-schemas-contracts/05-database-design.md`.
- RU autoscale is enabled on `sessions` and `artifacts`, the hottest containers.
- `prompts` and `promptVersions` are low-traffic and use lower RU.
- Query-side filtering keeps RU per request low.

## LLM concurrency and rate limits

- The LLM Router caps concurrent calls per provider and applies a per-caller
  rate limit.
- When a provider is at capacity, the router routes to the secondary provider or
  queues the request, based on policy.
- Circuit breakers prevent cascading failures when a provider degrades.

## Knowledge caching

- `JsonKnowledgeProvider` loads the Knowledge Base once per process and caches
  it, so repeated queries cost no I/O.
- The future `McpKnowledgeProvider` uses the `knowledgeCache` Cosmos container
  with a TTL to avoid repeated remote calls.

## Autoscaling rules

| Signal | Action |
|---|---|
| HTTP queue length high | Scale out API instances. |
| Generation queue depth high | Scale out workers. |
| Cosmos RU consumption high | Increase autoscale max. |
| LLM provider 429 rate | Throttle or route to secondary. |

## What is not shown

- Deployment topology: see `04-cross-cutting/02-deployment-architecture.md`.
- Cost implications of scaling: see `04-cross-cutting/08-enterprise-adoption.md`.
