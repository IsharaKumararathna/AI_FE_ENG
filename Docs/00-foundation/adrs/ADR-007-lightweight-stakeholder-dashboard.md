# ADR-007: Lightweight stakeholder dashboard in the MVP

Status: Accepted · Date: 2026-07-10

## Context

The Phase 1 MVP design (ADR-001, `06-frontend-architecture.md`) specified an
API-first client surface: REST API + CLI only, with the web portal deferred to a
later phase. The deferral rationale was sound: the generation contract was
unstable, and building UI against it would produce rework.

During implementation, a practical need emerged: business stakeholders (Product
Owners, Business Analysts) cannot evaluate pipeline output from JSON API
responses or CLI console text. They need a visual surface to:

1. Upload a prototype and trigger the pipeline without a terminal.
2. See the generated React rendered live (not as raw code).
3. Read the review report as a formatted scorecard, not a JSON object.
4. Download a printable PDF for analysis and sharing.

Without this surface, the platform is only usable by engineers — defeating the
vision's goal of enabling non-technical actors.

## Options considered

- **Keep API + CLI only (status quo).** Stakeholders cannot self-serve; every
  demo requires an engineer to run the CLI and interpret output. Slows adoption.
- **Build the full deferred portal now.** A separate React SPA with Vite, Entra
  ID auth, SignalR, and dogfooded DS components. Too much investment while the
  contract is still evolving; contradicts the deferral rationale.
- **Add a lightweight static dashboard served by the API.** A single HTML page
  with vanilla JavaScript that calls the existing REST API. No build tooling, no
  auth, no separate project. Sufficient for stakeholder demos and early
  evaluation. Replaceable by the full portal when it is built.

## Decision

Add a **lightweight stakeholder dashboard** as static files (`wwwroot/`) served
by the existing `Aife.Api` project. The dashboard is explicitly **not the
production portal** — it is a demo and evaluation tool that:

- Calls the existing REST API endpoints (no new API surface).
- Renders generated React in an iframe using CDN React + Babel standalone with
  mock DS component stubs for preview purposes.
- Provides a "Download PDF" button via the browser's print-to-PDF.
- Requires no authentication, no build step, no separate deployment.

### Scope

| Feature | In the dashboard | Deferred to full portal |
|---|---|---|
| Upload prototype | ✅ File picker + paste | ✅ (with drag-drop, progress) |
| Start session | ✅ One click | ✅ (with async polling + progress bar) |
| Live React preview | ✅ Mock components in iframe | ✅ (with real DS components) |
| Review report | ✅ Formatted scorecard | ✅ (with diff view, history) |
| Conformance report | ✅ Findings table | ✅ (with override actions) |
| PDF export | ✅ Browser print | ✅ (server-side PDF generation) |
| Authentication | ❌ None | ✅ Entra ID |
| SignalR live updates | ❌ Polling only | ✅ Real-time |
| Artifact browser | ✅ Code viewer | ✅ (with download, diff) |
| Generate from intent | ❌ (use CLI `--tree`) | ✅ (form-based UI) |

### What this does NOT change

- The REST API remains the primary integration surface — the dashboard is a
  consumer, not a peer.
- The CLI remains unchanged.
- The Clean Architecture boundary is preserved — the dashboard is in the API
  project's `wwwroot/`, not in Application or AI layers.
- The full portal design in `06-frontend-architecture.md` remains the target for
  a later phase. The dashboard is replaced, not extended, when the portal ships.

### Relationship to future phases

When the full portal is built (Phase 2+), the dashboard is **retired**. The
portal will be a separate React SPA project that dogfoods the platform's own
generated components, with Entra ID auth and SignalR. The API endpoints the
dashboard calls are the same endpoints the portal will call — no API changes
are needed when transitioning.

## Consequences

- The `Aife.Api` project now serves static files from `wwwroot/`. This is
  standard ASP.NET Core behavior (`UseDefaultFiles` + `UseStaticFiles`) and does
  not affect the API's REST endpoints or Clean Architecture.
- The dashboard uses CDN React + Babel standalone for live preview — this is
  client-side only and does not add dependencies to the .NET solution.
- Mock DS component stubs in the preview iframe are simplified visual
  approximations, not the real BUSKvalitet components. The generated React code
  itself imports the real components; only the preview rendering uses stubs.
- No new test coverage is required for the dashboard (it is a static consumer of
  the tested API). Integration tests verify the API endpoints it calls.
- The frontend architecture doc's "open question one" is now answered: the MVP
  includes a minimal portal (the dashboard), with the full portal deferred.

## Related

- ADR-001: .NET backend, Clean Architecture.
- `06-frontend-architecture.md`: full portal design (deferred), now amended to
  include the dashboard as an MVP client surface.
- `09-phase1-mvp-work-breakdown.md`: work breakdown, now includes the dashboard
  as a sub-task of E6 (API & CLI).
- `04-cross-cutting/02-deployment-architecture.md`: the dashboard is served by
  the existing API container; no new deployment unit.
