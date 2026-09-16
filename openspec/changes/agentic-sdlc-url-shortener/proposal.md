## Why

This change establishes a production-minded 48-hour prototype for an Agentic Software Engineering System that uses a URL shortener as a realistic engineering workload. The primary goal is not to build a shortener alone; it is to demonstrate a governed SDLC where requirements are interpreted, decomposed, validated, and revised through explicit workflow orchestration with traceability, policy guardrails, and human oversight. The prototype is designed to be reviewable, runnable, and demonstrably agentic without requiring external AI credentials to execute the repository.

## What Changes

- Introduce a modular monolith built on modern C#/.NET and ASP.NET Core for the application and orchestration APIs.
- Add a governed orchestration layer that manages workflow state, dependency-aware sequential and parallel DAG execution, synchronization barriers, approvals, bounded retries, compatible-provider fallback, safe-stop, rollback, and dynamic re-planning.
- Establish immutable, versioned engineering artifacts and structured Approval records early in the workflow so requirements, decisions, design, code, tests, and release readiness have auditable lineage.
- Add a deterministic provider as the mandatory execution path for repeatable, credential-free demonstrations and failure injection, plus a mandatory provider contract/configuration boundary for a future LLM provider.
- Keep a real external LLM adapter as SHOULD HAVE work only after deterministic end-to-end orchestration and required scenarios are complete and tested.
- Add a URL-shortener workload whose capabilities evolve through three scenarios: greenfield creation/redirect/validation/analytics, brownfield custom aliases, and ambiguous expiration requirements.
- Add workflow observability with audit-grade events, decision lineage, privacy-minimized analytics, and reliability metrics derived from persisted execution events.
- Add explicit release-readiness and policy gates so unsafe or unapproved actions block completion.
- Add a lightweight browser-accessible workflow dashboard as SHOULD HAVE, without allowing dashboard work to jeopardize core orchestration.
- Add setup, architecture, security-boundary, scenario, testing, assumptions, limitations, and prototype-versus-production documentation.

## 48-Hour Prioritization

### MUST HAVE

- Working URL shortener with greenfield, brownfield, and ambiguous-requirement scenarios.
- Requirement understanding, task decomposition, stateful DAG orchestration, sequential and dependency-aware parallel execution, and synchronization barriers.
- Versioned immutable artifacts, artifact dependencies, hashes, lineage, structured decision/Approval records, and dynamic re-planning.
- Human approval checkpoints, policy guardrails, bounded retry, precise compatible-provider fallback semantics, workflow/artifact rollback, and SafeStopped recovery semantics.
- Audit events, persisted reliability metrics, ReleaseReadiness gating, and independent automated orchestration verification.
- Deterministic provider, credential-free end-to-end execution, provider contract/configuration boundary, setup documentation, and architecture documentation.

### SHOULD HAVE

- Lightweight browser-accessible workflow dashboard.
- Docker support after core work is complete.
- One real external LLM provider integration after deterministic completion and required scenario tests pass.

### DEFERRED / PRODUCTION EVOLUTION

- Distributed workflow infrastructure, enterprise RBAC, sophisticated compliance policy catalogs, production deployment orchestration, full database migration rollback, multi-environment release recovery, advanced model routing, and large-scale scheduling.

## Capabilities

### New Capabilities
- `agentic-sdlc-orchestration`: Defines the orchestration, governance, artifact versioning, and trust-boundary model for the agentic SDLC system.
- `url-shortener-workload`: Defines the production-minded URL-shortener workload used to exercise the orchestrator and demonstrate the SDLC lifecycle.

### Modified Capabilities
- None.

## Impact

- Adds a new orchestration domain alongside the URL-shortener domain within a single ASP.NET Core application architecture.
- Introduces explicit persistence models for workflow state, node state, artifact metadata, approvals, policy results, validation evidence, and metrics in a single SQLite database with logical separation.
- Requires API surface for workflow orchestration, artifact management, review dashboards, and workflow execution operations, exposed via OpenAPI/Swagger.
- Introduces governance and policy enforcement boundaries that constrain agent capability and privilege to proposal/approval paths rather than unrestricted machine control.
- Creates a deterministic execution path for tests and demos without requiring external AI credentials, while preserving a provider contract for later reasoning-heavy integrations.
- Establishes CI through GitHub Actions, optional Docker execution, and independently testable scenarios for reviewer demonstration.
