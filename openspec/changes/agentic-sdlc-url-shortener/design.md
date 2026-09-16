## Context

This change introduces a governance-first agentic SDLC prototype built around a URL shortener workload. The design follows the approved architectural direction from the exploration: a modular monolith, ASP.NET Core Web API, SQLite-backed persistence, workflow DAG orchestration, deterministic validation/execution, and a pluggable provider abstraction. The purpose is to demonstrate a reviewable software engineering workflow rather than to build a broad production system.

## Goals / Non-Goals

**Goals:**
- Model the agentic SDLC as a governed workflow rather than a single linear agent chain.
- Support requirement normalization, task decomposition, validation, approvals, dynamic re-planning, and audit-grade execution history.
- Use a URL-shortener workload as a focused but production-minded demonstration domain.
- Support a deterministic provider for repeatable reviewer execution and failure injection.
- Keep the prototype runnable locally with minimal external infrastructure.
- Make MUST HAVE, SHOULD HAVE, and DEFERRED scope explicit so optional features cannot displace the core demonstration.

**Non-Goals:**
- Full multi-service distributed orchestration.
- Real-time multi-tenant enterprise deployment orchestration.
- Advanced production compliance frameworks beyond the prototype guardrails.
- Full database migration rollback for all production scenarios.
- Broad infrastructure automation or live cloud deployment management in the 48-hour prototype.

## Decisions

### 1. Modular monolith over distributed services
The prototype keeps one physical ASP.NET Core application with logical modules for orchestration, workflow execution, URL-shortener domain logic, persistence, validation, and observability. This reduces operational complexity, improves local runability, and keeps the engineering workflow easy to inspect by reviewers. A distributed architecture would create extra state synchronization and deployment complexity without materially improving the exercise demonstration.

**Alternatives considered:**
- Microservices with separate services for workflow execution and shortener logic.
- Full event-driven topology with multiple queues and stores.
- A single monolithic app without explicit module boundaries.

**Decision:** Keep a modular monolith with strong internal boundaries and explicit orchestration APIs.

### 2. Orchestrator owns workflow control; agents do bounded execution
The orchestration layer owns DAG planning, state transitions, dependency checks, policy enforcement, approvals, retries, re-planning, safe-stop, and rollback decisions. Agents are bounded workers that propose structured outputs and act within approved scopes. This separation keeps the system reviewable and prevents the control plane from becoming another autonomous agent.

**Alternatives considered:**
- A single autonomous agent that decides everything.
- A chain of LLM agents with no explicit control plane.
- A workflow engine without agent reasoning.

**Decision:** Treat the orchestrator as the authoritative workflow authority and agents as bounded workers.

### 3. Deterministic execution is mandatory; external AI is optional
The deterministic provider is mandatory for credential-free execution, repeatable demos, and automated validation. A pluggable LLM-backed provider is supported for requirement interpretation, ambiguity detection, decomposition suggestions, and summarization. Deterministic logic remains the trusted executor for policy evaluation, validation, rollback, and state transitions.

**Alternatives considered:**
- All execution through an LLM-backed provider.
- No deterministic provider, making demonstrations non-repeatable and credential-dependent.
- Hard-coding the AI provider into the orchestration core.

**Decision:** The deterministic provider and full credential-free path are MUST HAVE. The provider contract/configuration boundary is MUST HAVE. A real external LLM adapter is SHOULD HAVE and may only be implemented after deterministic orchestration and required scenarios are working and tested. External credentials are never required to clone, build, test, or demonstrate the repository.

### 4. Workflow graph with explicit state machines
The system models workflow execution as a DAG with entry gates, sequential and parallel nodes, synchronization points, and policy checks. Each node has a controlled lifecycle and only re-executes after explicit invalidation and re-planning. This prevents accidental reruns of successful work and preserves evidence quality.

**Alternatives considered:**
- Linear execution only.
- Re-running downstream nodes automatically with no state tracking.
- Unbounded agent-driven retries.

**Decision:** Use explicit workflow and node state machines enforced by the orchestrator.

### 5. Versioned engineering artifacts as first-class domain objects
The workflow persists EngineeringArtifact records that contain version, hash, lineage, producer node, producer execution, and validation state. These records create a stable contract across requirement, design, implementation, validation, and release-readiness stages. This enables impact analysis, provenance, re-planning, selective invalidation, and auditability.

**Alternatives considered:**
- Treating artifacts as simple files without an explicit version model.
- Persisting only runtime execution logs without version lineage.
- Keeping artifact metadata only in memory.

**Decision:** Persist artifact metadata and hashes in SQLite while referencing repository files by relative path and hash when practical.

### 6. Trust boundary uses structured ChangeSet and ActionProposal
Agents propose actions in structured form rather than direct unrestricted shell or filesystem access. These proposals are passed through policy evaluation, validation, and approval before a trusted deterministic executor performs privileged operations. This is the main risk-reduction boundary for arbitrary code execution, secrets leakage, and scope expansion.

**Alternatives considered:**
- Giving agents direct repo write access.
- Allowing arbitrary commands from LLM output.
- Not modeling privileged actions separately from general reasoning.

**Decision:** Require a structured proposal and trusted executor path for privileged actions.

### 7. Scenario-driven URL-shortener evolution
The URL shortener remains intentionally small but production-minded, while capabilities are introduced through the demonstration sequence. Greenfield establishes create, generated short code, redirect, URL validation, and basic click analytics. Brownfield adds custom aliases and demonstrates existing-code reasoning, impact analysis, selective invalidation/re-planning, and regression validation. The ambiguous scenario introduces the requirement "Make shortened URLs expire." and only adds expiration after consequential questions are clarified and approved.

**Alternatives considered:**
- A broader multi-domain business app.
- A very small toy app that under-specifies the operational complexities.
- An overbuilt distributed system with a simple workload.

**Decision:** Use a compact, evolving URL-shortener domain; expiration is a scenario outcome, not an unambiguous greenfield baseline feature.

### 8. Prototype rollback is workflow/artifact rollback, not full production deployment orchestration
The prototype implements workflow checkpoint rollback, artifact version rollback, and selective invalidation. It also supports code ChangeSet rollback where practical. Deployment rollback and large-scale production recovery are documented as future evolution, not core prototype scope.

**Alternatives considered:**
- Full deployment orchestration deep in the initial prototype.
- No rollback model beyond best effort.
- Requiring enterprise deployment complexity from day one.

**Decision:** Keep rollback grounded in workflow and artifact state, not deployment operations.

### 9. One physical SQLite database with logical separation
The prototype uses one physical SQLite database. URL-shortener tables and orchestration tables are logically separated through module ownership, naming, and persistence boundaries. This preserves simple local setup while keeping the domains clear enough for review and future extraction.

### 10. Lightweight dashboard as SHOULD HAVE
The prototype includes a lightweight browser-accessible workflow dashboard only after MUST HAVE orchestration and verification work is complete. It is not a substantial SPA and must not jeopardize core orchestration completion. A status API remains sufficient if dashboard time is unavailable.

### 11. Structured approvals for decision lineage
Approval records persist the workflow, plan revision, requested action or decision, risk classification, approver identity or role, decision, timestamp, rationale, and related artifact or ChangeSet references. Enterprise approval expiry, escalation, and RBAC infrastructure are deferred beyond prototype needs.

### 12. Explicit parallelism, fallback, and SafeStopped semantics
Independent ready nodes may execute concurrently when policy permits. A synchronization node remains blocked until every required predecessor satisfies its successful exit gate.

A node may fall back only to a provider it explicitly declares as compatible. Provider-unavailable transient failures receive bounded retry according to node policy; after exhaustion, the compatible fallback is used when configured and the provider transition is recorded. Without a compatible fallback, the workflow safe-stops or escalates according to policy. Deterministic logic is not treated as a universal substitute for reasoning-heavy LLM work.

SafeStopped is distinct from Failed. When safe-stopped, no additional executable nodes are scheduled, no pending privileged ChangeSet is applied, existing artifacts and evidence are preserved, the reason is persisted, the workflow remains inspectable, and authorized human recovery may initiate re-planning or another allowed recovery action.

Prototype MTTR means Mean Recovery Time: the average elapsed time from a recoverable workflow or node failure event until that workflow or node returns to a successful executable or completed state. Metrics are derived from persisted workflow events, never hard-coded.

Click analytics do not persist source or client IP addresses by default. They retain only short-code/link identity, timestamp, outcome, and optional justified non-sensitive request metadata. IP persistence is deliberately excluded because it is unnecessary for the demonstration and introduces avoidable privacy and data-governance concerns.

## Risks / Trade-offs

- [Agent trust risk] → Mitigation: enforce structured ActionProposal and trusted executor boundary; never give unrestricted shell or secrets access.
- [Workflow complexity risk] → Mitigation: keep the DAG simple, explicit, and persisted; avoid over-modeling distributed coordination in the prototype.
- [AI-provider dependence risk] → Mitigation: keep deterministic provider mandatory and review-friendly; LLM provider is optional and not required for demo success.
- [Replay and re-planning inconsistency] → Mitigation: require explicit invalidation, state transitions, and artifact lineage before rerunning a successful node.
- [Overengineering of business domain] → Mitigation: keep the URL-shortener workload focused and use orchestration complexity as the differentiator.
- [Prototype scope creep] → Mitigation: classify work into must-have, should-have, and deferred-production categories and resist broad implementation work.
- [Optional feature displacement] → Mitigation: schedule the dashboard, Docker, and real LLM adapter only after MUST HAVE orchestration and verification gates pass.

## Migration Plan

1. Establish the core project structure and one physical SQLite database with logical URL-shortener and orchestration persistence boundaries.
2. Establish immutable EngineeringArtifact, artifact dependency, versioning, hashing, and lineage foundations before higher-level orchestration and re-planning behavior.
3. Implement the greenfield URL-shortener baseline: create, generated code, redirect, validation, and basic privacy-minimized click analytics.
4. Implement workflow state machines, policy gates, DAG scheduling, dependency-aware parallel branches, and synchronization barriers.
5. Add structured Approval records, deterministic provider execution, failure injection, bounded retry, compatible fallback, SafeStopped handling, and rollback.
6. Implement the brownfield custom-alias scenario with impact analysis, selective invalidation, re-planning, and regression validation.
7. Implement the ambiguous expiration scenario with deterministic ambiguity detection, clarification persistence, revised requirement artifact, and resumed execution.
8. Add persisted events, derived reliability metrics including Mean Recovery Time, and ReleaseReadiness gating.
9. Complete independent orchestration verification, API/workload tests, setup documentation, and architecture/security documentation.
10. Only if MUST HAVE work is complete, add the lightweight dashboard, Docker support, and one real LLM adapter in that order.
