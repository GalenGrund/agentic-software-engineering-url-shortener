## Purpose

This capability defines the governed orchestration behavior for an Agentic Software Engineering System that converts requirements into reviewable engineering outcomes through explicit workflow control, bounded agent actions, and audit-grade evidence.

## ADDED Requirements

### Requirement: Workflow Governance
The system shall model engineering work as a persisted workflow graph with explicit dependencies, node states, approval gates, and policy enforcement rather than as a linear chat or simple agent chain.

#### Scenario: Greenfield feature delivery
- **WHEN** a new URL-shortener capability is proposed
- **THEN** the system shall normalize the requirement, create a dependency-aware plan, schedule executable work, validate outcomes, and only advance when policy and validation gates pass

#### Scenario: Brownfield change
- **WHEN** an upstream requirement or artifact changes
- **THEN** the system shall identify the affected subgraph, preserve unaffected valid evidence, invalidate only the impacted nodes, and create a revised plan revision while maintaining workflow lineage

#### Scenario: Ambiguous requirement
- **WHEN** a requirement is underspecified or materially ambiguous
- **THEN** the system shall detect the ambiguity, block the workflow from unsafe progression, surface unresolved questions, and require clarification before proceeding with high-risk implementation decisions

### Requirement: Explicit Workflow State Transitions
The system shall define legal workflow state transitions and reject illegal transitions without mutating the workflow into an invalid state.

#### Scenario: Illegal workflow transition
- **WHEN** a caller or agent requests a transition that is not legal from the current workflow state
- **THEN** the orchestrator shall reject the request, preserve the current state, and persist the rejection event

### Requirement: Dependency-Aware Parallel Execution
The system shall execute independent ready nodes concurrently when policy permits and shall enforce synchronization barriers for dependent nodes.

#### Scenario: Parallel ready branches
- **WHEN** multiple nodes are ready, independent, and permitted to run concurrently
- **THEN** the orchestrator shall schedule them as parallel branches and persist their execution lineage

#### Scenario: Synchronization barrier
- **WHEN** a downstream synchronization node has one or more required predecessors that have not satisfied successful exit gates
- **THEN** the synchronization node shall remain blocked until all required predecessors succeed

### Requirement: Explicit Node State Transitions
The system shall define legal node state transitions and shall reject illegal node transitions.

#### Scenario: Successful node rerun
- **WHEN** a completed node is requested to execute again without explicit invalidation in an approved plan revision
- **THEN** the orchestrator shall reject the rerun and preserve the successful evidence

### Requirement: Approval Decision Lineage
The system shall persist structured Approval records containing workflow and plan revision references, requested decision or action, risk, approver identity or role, decision, timestamp, rationale, and related artifact or ChangeSet references.

#### Scenario: Approval blocks execution
- **WHEN** a required approval is pending or rejected
- **THEN** the affected privileged or high-risk node shall remain non-executable until an authorized approval decision permits resumption

### Requirement: Bounded Agent Autonomy
Agents shall produce structured proposals and decisions within explicitly bounded task scopes, while the orchestrator owns state transitions, validation, approval, and execution authority.

#### Scenario: Proposed privileged action
- **WHEN** an agent proposes a privileged action such as a repository change, schema mutation, or deployment-related operation
- **THEN** the request shall be evaluated by policy and validation before trusted deterministic execution is allowed

#### Scenario: Low-risk reasoning work
- **WHEN** an agent performs non-privileged analysis or documentation work
- **THEN** it may proceed within its approved task context without requiring a separate human approval step

### Requirement: Versioned Engineering Artifacts
The system shall represent engineering outputs as versioned EngineeringArtifact records with immutable outputs, lineage, hashes, and validation status so downstream work can reason over provenance and change impact.

#### Scenario: Artifact revision
- **WHEN** a design or implementation artifact is superseded by a newer version
- **THEN** the workflow shall preserve the previous version as lineage, mark the new version as current, and allow selective invalidation of downstream dependents

### Requirement: Dynamic Re-planning
The system shall support dynamic re-planning when upstream artifacts or requirements change, while preserving unaffected valid work and recording the plan revision lineage.

#### Scenario: Upstream change triggers re-plan
- **WHEN** a requirement or artifact version changes and affects downstream work
- **THEN** the system shall identify impacted nodes, preserve unaffected completed work, generate a revised DAG or plan revision, and execute only the necessary downstream work

### Requirement: Policy and Security Guardrails
The system shall enforce prototype guardrails for repository scope, secrets hygiene, risk classification, change control, and release readiness.

#### Scenario: Policy violation
- **WHEN** an agent or workflow attempts a forbidden privileged action or a high-risk action without approval
- **THEN** the system shall reject or block the action and record the policy evaluation result

### Requirement: Observability and Auditability
The system shall persist workflow events sufficient to reconstruct execution lineage, approvals, failures, retries, and state transitions for review and metrics.

#### Scenario: Audit reconstruction
- **WHEN** a reviewer inspects a workflow execution
- **THEN** the system shall make available the workflow ID, plan revision, node lineage, agent/provider identity, timestamps, approvals, retries, validation outcomes, and final state

### Requirement: Reliability Metrics
The system shall derive workflow and node reliability metrics from actual workflow execution events.

#### Scenario: Post-run metrics
- **WHEN** a workflow finishes or fails
- **THEN** the system shall derive metrics from persisted workflow events, including success rate, retry frequency, rollback frequency, safe-stop frequency, Mean Recovery Time, approval latency, and end-to-end latency

#### Scenario: Mean Recovery Time
- **WHEN** a recoverable workflow or node failure is followed by successful executable or completed state
- **THEN** Mean Recovery Time shall be calculated as the elapsed time from the failure event to that recovered state, averaged across recorded recoveries

### Requirement: Release Readiness Gate
The system shall include an explicit ReleaseReadiness gate that consumes validation evidence, policy results, and approval records before allowing completion.

#### Scenario: Release approval
- **WHEN** the workflow reaches the release-readiness stage
- **THEN** it shall not pass unless required validations, policies, approvals, and artifacts are satisfied

## ADDED Requirements

### Requirement: Deterministic Execution Support
The system SHALL support a deterministic provider for repeatable reviews, credential-free execution, and failure injection without pretending that deterministic rule execution is AI reasoning.

#### Scenario: Demo-safe execution
- **WHEN** a reviewer runs the demonstration scenarios without external credentials
- **THEN** the deterministic provider shall enable repeatable execution and validation paths

### Requirement: Deterministic Scenario Semantics
The deterministic provider SHALL implement repeatable ambiguity, failure, retry, fallback, approval, and recovery outcomes needed by the required scenarios without relying on nondeterministic LLM output.

#### Scenario: Expiration ambiguity
- **WHEN** the deterministic provider receives the requirement "Make shortened URLs expire."
- **THEN** it shall identify the required clarification questions and block implementation nodes until approved decisions are supplied

### Requirement: Failure Handling and Recovery
The system SHALL support bounded retry, explicitly compatible provider fallback, safe stop, human escalation, and workflow/artifact rollback for recoverable and policy-blocked failures.

#### Scenario: Transient failure
- **WHEN** an external provider or validation step fails transiently
- **THEN** the workflow shall apply bounded retry according to policy, then use a compatible fallback only when the node explicitly declares one, recording the provider transition

#### Scenario: No compatible fallback
- **WHEN** retry is exhausted and no explicitly compatible fallback provider is configured
- **THEN** the workflow shall safe-stop or escalate according to policy and shall not pretend deterministic logic can replace incompatible reasoning work

#### Scenario: Policy-blocked action
- **WHEN** a privileged action violates guardrails or approval requirements
- **THEN** the workflow shall stop safely, record the reason, and escalate when required

### Requirement: SafeStopped State
SafeStopped SHALL be a distinct terminal or recoverable workflow state from Failed.

#### Scenario: Safe stop preservation
- **WHEN** the orchestrator safe-stops a workflow
- **THEN** it shall schedule no additional executable nodes, apply no pending privileged ChangeSet, preserve artifacts and evidence, persist the reason, keep the workflow inspectable, and permit authorized human recovery to initiate re-planning or another allowed recovery action

### Requirement: Artifact Impact and Selective Invalidation
The system SHALL use immutable artifact versions, dependency relationships, hashes, and lineage to determine impact and invalidate only affected downstream nodes.

#### Scenario: Preserve unaffected work
- **WHEN** an upstream artifact changes
- **THEN** the system shall preserve unaffected completed work, invalidate affected dependents, create a revised plan lineage, and execute only required downstream work

### Requirement: Independent Orchestration Verification
The system SHALL have automated tests independent of the visual demonstration covering legal and illegal workflow/node transitions, dependency-aware scheduling, parallel branches, synchronization barriers, bounded retry and exhaustion, compatible fallback and no-fallback behavior, SafeStopped behavior, approval blocking/resumption, policy rejection, artifact immutability/version lineage, impact analysis, selective invalidation, preservation of unaffected work, plan revision lineage, workflow/artifact rollback, and ReleaseReadiness evidence requirements.

#### Scenario: Orchestration semantics verification
- **WHEN** the orchestration test suite runs
- **THEN** it shall verify the listed semantics without depending on a browser dashboard or nondeterministic external provider

## MODIFIED Requirements

No existing requirements are modified in this change because this is a new system capability.
