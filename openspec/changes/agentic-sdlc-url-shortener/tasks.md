## 1. Foundation and Architecture [MUST HAVE]

- [x] 1.1 Create the ASP.NET Core solution structure and modular boundaries for orchestration, workflow, URL-shortener domain, persistence, and validation layers
- [x] 1.2 Configure one physical SQLite database, EF Core models, and logical separation between URL-shortener and orchestration data
- [x] 1.3 Configure OpenAPI/Swagger and local run configuration for API and workflow visibility
- [x] 1.4 Add GitHub Actions workflow for build and automated tests and verify CI command execution locally
- [x] 1.5 Define the deterministic provider and external-provider contract/configuration boundary without requiring external credentials

## 2. Artifact Foundations [MUST HAVE]

- [x] 2.1 Implement immutable EngineeringArtifact records with content hashing, versioning, validation status, and producer provenance
- [ ] 2.2 Implement artifact dependency records and lineage queries before higher-level re-planning behavior
- [ ] 2.3 Implement artifact revision rules that preserve prior versions and reject mutation of immutable evidence

## 3. Domain and Workload Model [MUST HAVE]

- [ ] 3.1 Implement the greenfield domain model for short links, generated codes, validation, basic analytics, and later enhancement metadata
- [x] 3.2 Implement URL validation and unique code generation for the greenfield baseline
- [x] 3.3 Implement create-short-link API and redirect API with consistent error contracts and testable outcomes
- [x] 3.4 Implement privacy-minimized analytics without source/client IP persistence by default
- [ ] 3.5 Implement brownfield custom aliases and regression behavior
- [ ] 3.6 Implement expiration only through the clarified ambiguous-requirement workflow

## 4. Workflow Orchestration Core [MUST HAVE]

- [ ] 4.1 Implement workflow, plan revision, node, dependency, policy evaluation, Approval, and event persistence models
- [ ] 4.2 Implement legal workflow and node state transitions with rejection of illegal transitions
- [ ] 4.3 Implement dependency-aware DAG scheduling with sequential and concurrent independent branches
- [ ] 4.4 Implement synchronization barriers that remain blocked until all required predecessor exit gates succeed
- [ ] 4.5 Implement entry/exit gates, validation hooks, and ReleaseReadiness gating

## 5. Agent and Provider Execution [MUST HAVE]

- [ ] 5.1 Define the common agent execution contract and provider compatibility declarations
- [ ] 5.2 Implement the deterministic provider for repeatable testing, ambiguity handling, failure injection, and credential-free end-to-end execution
- [ ] 5.3 Add structured ActionProposal and ChangeSet handling before privileged execution is allowed
- [ ] 5.4 Record provider transitions and reject fallback to providers not explicitly declared compatible

## 6. Policy, Approval, Retry, and Recovery [MUST HAVE]

- [ ] 6.1 Implement risk classification and policy evaluation for low-, medium-, and high-risk actions
- [ ] 6.2 Implement structured Approval persistence and gating for high-risk operations and material scope changes
- [ ] 6.3 Implement bounded retries, retry exhaustion, compatible fallback, no-fallback behavior, and human escalation flow
- [ ] 6.4 Implement distinct SafeStopped behavior: stop scheduling, do not apply pending privileged ChangeSets, preserve evidence, persist reason, and permit authorized recovery
- [ ] 6.5 Implement workflow/artifact rollback and recovery checkpoints

## 7. Dynamic Re-planning and Impact Analysis [MUST HAVE]

- [ ] 7.1 Implement dependency impact analysis to identify downstream artifact and node invalidation sets
- [ ] 7.2 Implement plan revision generation with lineage and preservation of unaffected valid work
- [ ] 7.3 Verify that successful nodes cannot rerun without explicit invalidation and approved re-plan
- [ ] 7.4 Implement the brownfield custom-alias re-planning scenario
- [ ] 7.5 Implement the ambiguous expiration scenario: deterministic questions, blocked state, persisted clarification, revised requirement artifact, and resumed execution

## 8. Independent Orchestration Verification [MUST HAVE]

- [ ] 8.1 Verify legal and illegal workflow state transitions and legal and illegal node state transitions
- [ ] 8.2 Verify successful nodes cannot rerun without invalidation, dependency-aware scheduling, parallel branches, and synchronization barriers
- [ ] 8.3 Verify bounded retry, retry exhaustion, compatible fallback, no-compatible-fallback behavior, and SafeStopped semantics
- [ ] 8.4 Verify approval blocking/resumption, policy rejection, and ReleaseReadiness rejection without required validation/policy/approval evidence
- [ ] 8.5 Verify artifact immutability, version lineage, dependency impact analysis, selective invalidation, preservation of unaffected completed work, and plan revision lineage
- [ ] 8.6 Verify workflow/artifact rollback and persisted event-derived reliability metrics, including Mean Recovery Time
- [ ] 8.7 Verify greenfield, brownfield, and ambiguous scenarios through deterministic end-to-end tests

## 9. Observability and Documentation [MUST HAVE]

- [ ] 9.1 Implement workflow event capture and metrics derivation for success, retries, rollback, SafeStopped, approval latency, Mean Recovery Time, and end-to-end latency
- [ ] 9.2 Write README, architecture overview, security/trust-boundary explanation, scenario walkthroughs, testing approach, privacy decision, assumptions, limitations, and prototype-versus-production trade-offs
- [ ] 9.3 Verify the repository can be cloned, built, tested, and demonstrated without external AI credentials

## 10. Optional Enhancements [SHOULD HAVE]

- [ ] 10.1 Add a lightweight browser-accessible workflow dashboard for workflow state, DAG status, lineage, approvals, and metrics after MUST HAVE work is complete
- [ ] 10.2 Add Docker support if core work is complete
- [ ] 10.3 Add one real external LLM provider adapter only after deterministic end-to-end orchestration and required scenario tests pass

## 11. Production Evolution [DEFERRED]

- [ ] 11.1 Document future distributed workflow infrastructure, enterprise RBAC, sophisticated compliance policy catalogs, production deployment orchestration, full database migration rollback, multi-environment release recovery, advanced model routing, and large-scale scheduling
