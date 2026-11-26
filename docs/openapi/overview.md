Public machine-to-machine API for discovering Education Estates Directorate (EED) survey schemas and submitting validated estate data returns.

## Authentication

All schema discovery and submission endpoints require a **DfE Find and Use an API (FaUAPI)** subscription key passed in the `Ocp-Apim-Subscription-Key` header.

- Subscription keys identify the calling organisation / surveyor context and are recorded on submission audit trails.
- `GET /about` is intentionally anonymous for readiness and health monitoring.
- When OAuth 2.0 application flow (`client_credentials`) is enabled on the FaUAPI product, callers may also supply a bearer token as configured in the `securitySchemes` section.

## Submission Lifecycle

1. **Discover active schemas** using `GET /schemas` or `GET /schemas/{schemaName}` to find the active `version` and inspect fields.
2. **Inspect contract rules** using `GET /schemas/{schemaName}/versions/{version}` (pass `includeRawJsonSchema=true` if you need the full JSON Schema document).
3. **Create a submission session** using `POST /submissions/{schemeName}` to pin a `submissionId` to the active (or explicitly requested) schema version.
4. **Upload incremental drafts** using `PUT /submissions/{submissionId}` with full or partial JSON payloads. Draft updates merge chronologically and return soft validation feedback (`pendingErrors`) without blocking intermediate saves.
5. **Check readiness** using `GET /submissions/{submissionId}/check` to merge uploaded drafts, evaluate quality rules and identifier extraction, and review `isReadyToFinalise` and `nextSteps`.
6. **Finalise** using `POST /submissions/{submissionId}/finalise` to run strict schema, quality-rule, and identifier validation and promote the payload to the processed landing zone.

## Response Envelope & Errors

All responses use the `ApiResponse<T>` envelope:

- **Success (`200` / `201`)**: `successful: true` with payload in `data`.
- **Failure (`400` / `404` / `409` / `422`)**: `successful: false` with an RFC 9457 `ProblemDetails` object in `problem` (including machine-readable `title` error codes and optional per-field `errors` extensions).
