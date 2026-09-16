## Purpose

This capability defines the production-minded URL-shortener workload used to exercise the Agentic Software Engineering System and demonstrate requirement understanding, orchestration, validation, and release readiness.

## ADDED Requirements

### Requirement: Greenfield Short URL Baseline
The initial greenfield baseline SHALL support creation of shortened URLs from validated target URLs, generated short-code creation, redirect resolution, and basic click analytics.

#### Scenario: Valid new short URL
- **WHEN** a caller provides a valid target URL without a custom alias
- **THEN** the system shall generate a unique short code, persist the target and baseline metadata, and return the created resource information

#### Scenario: Invalid target URL
- **WHEN** a caller provides an invalid or unsafe target URL
- **THEN** the system shall reject the request with a validation error and shall not persist the short URL record

### Requirement: Redirect Resolution
The system SHALL resolve a short code to a target URL and return a redirect response when the short URL is active and valid.

#### Scenario: Active short URL
- **WHEN** a short code exists and is not expired or disabled
- **THEN** the system shall return an HTTP redirect to the target URL and record click analytics for the request

#### Scenario: Missing short code
- **WHEN** a short code does not exist or is otherwise invalid
- **THEN** the system shall return a not found or invalid redirect error without silently exposing internal state

### Requirement: Brownfield Custom Alias Enhancement
The brownfield enhancement SHALL add optional custom aliases while enforcing uniqueness and safe validation of the alias and target URL.

#### Scenario: Custom alias provided
- **WHEN** a caller specifies a custom alias that is already in use or violates constraints
- **THEN** the system shall reject the request and require a valid alternative alias

#### Scenario: Brownfield alias evolution
- **WHEN** the custom-alias requirement is introduced to the existing greenfield baseline
- **THEN** the workflow shall analyze existing code, identify impacted artifacts and tests, selectively invalidate affected work, re-plan the change, preserve unaffected evidence, and run regression validation

### Requirement: Ambiguous Expiration Enhancement
The system SHALL add expiration only after the ambiguous requirement "Make shortened URLs expire." is clarified and approved.

#### Scenario: Expiration ambiguity is blocked
- **WHEN** the workflow receives "Make shortened URLs expire."
- **THEN** it shall identify at least the default expiration duration, whether caller-defined expiration is permitted, maximum expiration duration, behavior when an expired URL is requested, and analytics retention after expiration as unresolved questions; it shall enter a blocked or waiting state and keep implementation nodes non-executable

#### Scenario: Approved expiration decisions resume work
- **WHEN** a human provides and approves decisions for all required expiration questions
- **THEN** the workflow shall persist the decisions, create a revised normalized requirement artifact, re-plan the affected work, and resume execution using the approved decisions

#### Scenario: Expired short URL after clarification
- **WHEN** a short URL has an approved expiration policy and its expiration time is reached
- **THEN** the system shall apply the approved policy and return the approved expired-link behavior without exposing internal state

### Requirement: Click Analytics and Reliability Controls
The system SHALL capture click analytics and basic reliability/security controls sufficient for a production-minded prototype without creating operational complexity beyond the 48-hour scope.

#### Scenario: Redirect analytics
- **WHEN** a short code is used
- **THEN** the system shall record minimal metadata such as short-code/link identity, timestamp, outcome, and optional justified non-sensitive request metadata, but shall not persist source or client IP addresses by default

#### Scenario: Basic protected behavior
- **WHEN** invalid input or unsafe requests are made
- **THEN** the system shall validate and reject them with deterministic error responses and without exposing sensitive internals

### Requirement: API and Error Contract
The system SHALL provide a stable API contract with explicit request and response behavior, including validation failures and missing-resource conditions.

#### Scenario: API validation failure
- **WHEN** a client submits malformed or invalid input
- **THEN** the API shall return structured validation errors that can be consumed by automated tests and documentation tools

#### Scenario: Not found or invalid redirect
- **WHEN** a client requests a missing or, after clarification, expired short code
- **THEN** the API shall return an appropriate HTTP error status and consistent payload contract

## MODIFIED Requirements

No existing requirements are modified in this change because this is a new system capability.
