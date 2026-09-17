# Reviewer Demo Guide

This guide uses the current API and Swagger surface. Start the application with:

```powershell
dotnet run --project AgenticSoftwareEngineering.Api
```

Open `https://localhost:5001/swagger` or the URL printed by ASP.NET Core. The exact development port may vary with local launch settings; Swagger is the easiest way to inspect the live contract.

## 1. URL Shortener

Create a link:

```http
POST /api/short-links
Content-Type: application/json

{"targetUrl":"https://example.com/reviewer"}
```

Use the returned `shortUrl`, or request `GET /{shortCode}` directly. Invalid targets return the structured validation error; unknown codes return the structured not-found response. Click analytics are privacy-minimized and do not persist client IP.

## 2. Greenfield Workflow

Create a workflow:

```http
POST /api/workflows/greenfield
Content-Type: application/json

{"requirement":"Create a short URL"}
```

Save `workflowId`. Advance it repeatedly:

```http
POST /api/workflows/{workflowId}/advance
```

Inspect:

```http
GET /api/workflows/{workflowId}
```

The status shows Normalize Requirement, Decompose / Plan, the Architecture / Design and Implementation Preparation branches, Validation, Release Readiness, dependencies, executions, artifacts, validations, and events.

## 3. High-Risk Approval and Change Governance

Request a high-risk workflow:

```http
POST /api/workflows/greenfield
Content-Type: application/json

{"requirement":"Create a short URL","requiresHighRiskApproval":true}
```

Advance until the workflow is `WaitingForApproval`. Inspect `approvals`, `actionProposals`, `changeSets`, and `changeSetAuthorizations` in the status response. Proposal mode has occurred, but Apply mode has not.

Approve the exact approval:

```http
POST /api/workflows/{workflowId}/approvals/{approvalId}/decision
Content-Type: application/json

{"approved":true,"rationale":"Approved the exact bounded ChangeSet."}
```

The response exposes the exact fingerprint, authorization mechanism, approval ID, applied execution lineage, and `Applied` proposal state. Rejecting the approval instead produces SafeStopped evidence and no privileged Apply request.

## 4. Metrics

Inspect persisted-evidence metrics:

```http
GET /api/workflows/{workflowId}/metrics
```

The response includes completion, workflow latency, provider execution count/latency, retry/fallback counts and rates, SafeStop, approval count/wait, and Mean Recovery Time where recovery evidence exists.

## 5. Brownfield Revision

First complete a greenfield workflow and choose an existing artifact, such as the version-1 `architecture-design` artifact. Revise it:

```http
POST /api/workflows/{workflowId}/brownfield/artifacts/{artifactId}/revise
Content-Type: application/json

{"contentReference":"architecture/reviewer-v2","contentHash":"reviewer-v2-hash"}
```

Inspect the returned PlanRevision history, superseded artifact, impact-analysis event, invalidated nodes, preserved succeeded nodes, and dependency lineage. Advance the workflow until completion.

Rollback to a prior artifact version:

```http
POST /api/workflows/{workflowId}/rollback
Content-Type: application/json

{"artifactId":"<prior-artifact-id>"}
```

Rollback creates a new immutable artifact version, preserves history, invalidates affected downstream work, and records rollback/replanning evidence. This is workflow/artifact rollback, not Git, database-backup, or deployment rollback.

Custom aliases are intentionally deferred; this brownfield demonstration is generic artifact revision and re-planning.

## 6. Ambiguous Requirement

Create the bounded ambiguity scenario:

```http
POST /api/workflows/greenfield
Content-Type: application/json

{"requirement":"Make shortened URLs expire."}
```

The workflow enters `Blocked`, exposes `clarificationStatus: Required`, records `RequirementAssessed` and `ClarificationRequired`, and invokes no engineering provider.

Provide bounded clarification:

```http
POST /api/workflows/{workflowId}/clarification
Content-Type: application/json

{"clarification":"Expire links after 30 days using UTC timestamps."}
```

The status then exposes `ClarificationProvided`, `RequirementReassessed`, `ClarificationResolved`, and a hashed `clarified-requirement` artifact. Normalize Requirement receives the original requirement plus the accepted clarification as its effective requirement. Actual expiration enforcement remains deferred.

## 7. Test and Reproducibility Commands

The accepted Gate 6 baseline reports 96 passing tests:

```powershell
dotnet restore AgenticSoftwareEngineering.slnx
dotnet build AgenticSoftwareEngineering.slnx --configuration Release --no-restore
dotnet test AgenticSoftwareEngineering.slnx --configuration Release --no-restore
```

Gate 8 will independently verify these steps from a clean clone. Gate 7 does not claim that clean-room verification is complete.
