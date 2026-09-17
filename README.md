# Agentic Software Engineering System

This repository demonstrates a governed agentic software engineering workflow using a URL shortener as a bounded engineering workload. The URL shortener is deliberately small; the primary system is the persisted orchestration control plane around it.

The prototype demonstrates requirement assessment, deterministic decomposition, a persisted DAG, parallel-ready branches, synchronization, structural gates, artifact lineage, policy and approval, exact ActionProposal/ChangeSet authorization, bounded retry and fallback, SafeStop, replanning, selective invalidation, rollback, ambiguity clarification, and persisted reliability metrics.

## Quick Start

Prerequisite: .NET SDK 10.0.x.

```powershell
dotnet restore AgenticSoftwareEngineering.slnx
dotnet build AgenticSoftwareEngineering.slnx --configuration Release --no-restore
dotnet test AgenticSoftwareEngineering.slnx --configuration Release --no-restore
dotnet run --project AgenticSoftwareEngineering.Api
```

The API uses one local SQLite database, applies pending EF migrations at startup, and exposes Swagger at `/swagger`. This startup migration behavior is a prototype convenience; production deployments should use a governed migration process. The deterministic provider is the default execution path. No external AI key, cloud account, external database, or paid service is required.

The accepted Gate 6 baseline has 93 passing tests. A clean-clone verification is reserved for Gate 8 and has not been claimed here.

## Reviewer Path

Use Swagger to exercise the API, or follow [docs/demo-guide.md](docs/demo-guide.md). The workflow status endpoint exposes nodes, dependencies, executions, artifacts, approvals, proposals, ChangeSets, authorization evidence, plan revisions, and audit events. Metrics are available from `GET /api/workflows/{workflowId}/metrics`.

Architecture and trust-boundary details are in [docs/architecture.md](docs/architecture.md).

## Scope

Implemented scenarios are greenfield execution, generic brownfield artifact revision/replanning and rollback, and bounded ambiguous-requirement clarification. Custom aliases, URL expiration behavior, a visual dashboard, Docker support, and an external LLM adapter remain intentionally deferred. These are scope decisions, not hidden runtime dependencies.
