# Agentic Software Engineering System

This repository demonstrates governed agentic software engineering using a URL shortener as a bounded engineering workload. The URL shortener is deliberately small. The primary architectural subject is the persisted orchestration control plane around it, implemented as a modular-monolith ASP.NET Core/.NET 10 prototype with SQLite.

The prototype demonstrates requirement assessment, deterministic decomposition, a persisted directed acyclic graph (DAG), parallel-ready branches, synchronization barriers, structural gates, artifact lineage, policy and approval, exact ActionProposal/ChangeSet authorization, bounded retry and fallback, SafeStop, replanning, selective invalidation, auditable rollback, ambiguity clarification, and persisted reliability metrics.

## Development Approach: OpenSpec Loop Development

The prototype itself was developed using OpenSpec Loop Development, applying a specification-driven loop to proposal, design, task decomposition, implementation, and verification. The change package is [openspec/changes/agentic-sdlc-url-shortener](openspec/changes/agentic-sdlc-url-shortener/).

Its proposal and design intent, specifications, task decomposition, implementation and verification progression, and intentionally deferred scope provide a substantial development evidence trail. OpenSpec is the process used to build and evolve this prototype; it does not execute the runtime orchestration system. The prototype demonstrates governed agentic software engineering, while its own development used a governed specification-driven loop.

## AI and Agentic Execution

The runtime separates three concerns:

- **Intelligence:** the provider performs bounded engineering reasoning and generation. The current provider is deterministic by design, making execution reproducible, testable, reviewable, and runnable without external AI credentials.
- **Orchestration:** the control plane determines eligible work, dependency readiness, synchronization, validation, retries, replanning, and lifecycle state. Provider success is not equivalent to node success; the orchestrator validates output and evidence before completing a node.
- **Authority:** policy evaluation determines the authorization path, including human approval for high-risk actions. The provider or agent may propose actions, but it does not own authorization or orchestration authority.

The replaceable provider abstraction is the intelligence boundary. A future LLM-backed provider could perform bounded tasks such as requirement interpretation, decomposition, codebase reasoning, architecture and design recommendations, implementation proposals, test generation, documentation generation, and failure analysis or remediation proposals. These are extension points, not current external-AI integrations.

For privileged operations, the governed path is: provider proposes, the orchestrator materializes and fingerprints a ChangeSet, policy and human authorization occur, the orchestrator validates that authorization, and the provider may then apply the authorized operation.

## Architecture at a Glance

Requirement -> governed workflow and persisted DAG -> bounded provider execution -> persisted artifacts and evidence -> validation and structural gates -> release readiness.

The control plane provides dependency-aware execution, parallel-ready branches without claiming concurrent worker execution, synchronization barriers, human approval for high-risk actions, bounded retry/fallback/SafeStop behavior, artifact lineage, dynamic replanning with selective invalidation, auditable rollback, and persisted events and metrics. Rollback creates a new artifact version containing selected prior content. It does not erase history and is not production deployment rollback.

## Quick Start

Prerequisite: .NET SDK 10.0.x.

```powershell
dotnet restore AgenticSoftwareEngineering.slnx
dotnet build AgenticSoftwareEngineering.slnx --configuration Release --no-restore
dotnet test AgenticSoftwareEngineering.slnx --configuration Release --no-restore
dotnet run --project AgenticSoftwareEngineering.Api
```

The API uses one local SQLite database, applies pending EF migrations at startup, and exposes Swagger at `/swagger`. This startup migration behavior is a prototype convenience; production deployments should use a governed migration process. The deterministic provider is the default execution path. No external AI key, cloud account, external database, or paid service is required.

The final verified baseline has 96 passing tests. Clean-clone verification confirmed that the repository can be restored, built, tested, migrated, and demonstrated without external AI credentials.

## Reviewer Path

1. Start with this README for orientation and the Quick Start.
2. Read [docs/architecture.md](docs/architecture.md) for architecture, trust boundaries, terminology, and scenario workflows.
3. Follow [docs/demo-guide.md](docs/demo-guide.md) for an executable reviewer walkthrough.
4. Review the [OpenSpec change package](openspec/changes/agentic-sdlc-url-shortener/) for development evidence.

Use Swagger to exercise the API. The workflow status endpoint exposes nodes, dependencies, executions, artifacts, approvals, proposals, ChangeSets, authorization evidence, plan revisions, and audit events. Metrics are available from `GET /api/workflows/{workflowId}/metrics`.

## Scope and Intentional Deferrals

Implemented scenarios are greenfield workflow execution, generic brownfield artifact revision, impact analysis, selective invalidation, replanning and artifact/workflow rollback, and ambiguous-requirement detection with human clarification before provider execution. Custom aliases, actual URL expiration behavior, a visual dashboard, Docker support, and an external LLM adapter remain intentionally deferred. These are intentional scope boundaries for this rapid engineering prototype, not hidden runtime dependencies.
