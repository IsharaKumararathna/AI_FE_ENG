# ADR-002: Multi-provider LLM Router from day one

Status: Accepted · Date: 2026-07-07

## Context

AI stages call an LLM for analysis, mapping, generation, and review. The vision
requires extensible AI providers. Locking the MVP to one provider creates
switching cost later and prevents cost or capability routing. The initiative
owner selected multi-provider support from day one.

## Options considered

- Single provider hardcoded in each stage. Simplest, but couples every stage to
  one vendor and blocks routing.
- Multi-provider router with an `ILlmProvider` abstraction and at least two
  registered providers.
- Multi-provider but only via configuration, with no router abstraction.

## Decision

Define `ILlmProvider` in `Aife.Application`. Implement provider clients in
`Aife.Infrastructure`. Introduce `LlmRouter` in `Aife.Application` as the only
component AI stages call. Register Azure OpenAI as the default provider and one
secondary provider. The secondary provider is pending confirmation; Anthropic
Claude is the recommendation.

## Consequences

- AI stages depend on `LlmRouter`, never on a specific provider client, so
  adding or swapping providers does not touch stage code.
- A capability matrix is required so the router can match a stage's needs
  (context window, function calling, vision) to a provider.
- Provider-specific quirks (token limits, response shapes, retry behavior) are
  isolated in each `ILlmProvider` implementation.
- Configuration and secret management for multiple providers must be handled in
  `04-cross-cutting/04-security.md`.
