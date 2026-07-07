# CI/CD Strategy

Status: Approved · Date: 2026-07-07 · Version: 1.0
Vision deliverable: 24 (CI/CD Strategy)

## Summary

CI builds, tests, validates schemas and docs, and builds the container. CD
deploys to dev on merge, to stage on tag, and to prod on approval. Infrastructure
is Bicep. Branches follow the `aife-<issue-number>` convention from the repository
key.

## Branch and issue model

- Issues are created in the board; each gets a number.
- Work happens on a branch named `aife-<issue-number>` (for example `aife-42`).
- Changes merge to `main` via pull request with a required build.
- Releases are tagged `v<major>.<minor>.<patch>`.

## Pipeline stages

| Stage | Actions | Gates |
|---|---|---|
| Build | `dotnet build`, restore, compile. | Build must pass. |
| Test | Unit, contract, integration tests. | All tests green. |
| Schema validation | Validate sample data against JSON Schemas. | Validation passes. |
| Doc lint | Markdown lint, Mermaid syntax check. | Lint passes. |
| Container build | Build API image, scan for vulnerabilities. | Scan clean. |
| Deploy dev | Deploy to dev on merge. | Automatic. |
| Deploy stage | Deploy to stage on tag. | Automatic. |
| Deploy prod | Deploy to prod. | Manual approval. |

## Infrastructure as code

Bicep modules provision Container Apps, Cosmos DB, Blob Storage, Key Vault, and
Azure OpenAI. A `main.bicep` parameter file per environment applies
environment-specific values. Infrastructure deployment runs in the same pipeline
before the app deployment when infra changed.

## Quality gates

- Pull requests require a passing build and test run.
- Schema and golden-file tests block the merge on regression.
- Prompt evaluation runs nightly on the current prompt versions and fails the
  build if a published version regresses below threshold.

## What is not shown

- Test detail: see `04-cross-cutting/05-testing-strategy.md`.
- Secret handling in the pipeline: see `04-cross-cutting/04-security.md`.
