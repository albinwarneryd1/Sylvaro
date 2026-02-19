# Sylvaro

Sylvaro is a compliance operations platform for AI systems with deterministic assessment logic, evidence mapping, human sign-off, RAG-backed context retrieval, and export/webhook workflows.

## What is included

- `src/Sylvaro.Api` ASP.NET Core API with JWT auth, RBAC, tenant isolation, audit log, policy engine, assessments, findings/actions, evidence map/gaps, JSON+PDF exports, and webhook integrations.
- `src/Sylvaro.Web` Blazor web app with login/register, dashboard, tenant settings, AI systems, diff view, actions kanban, evidence gaps/search, integration controls, and audit views.
- `src/Sylvaro.Infrastructure` EF Core/PostgreSQL persistence, seeded demo data, provider-agnostic AI draft layer (Local/OpenAI/Azure-compatible), RAG indexing/search, policy pack evaluation.
- `policy-packs/` versioned compliance-as-code JSON packs.
- `prompts/` versioned prompt templates for structured JSON generation.
- `reference-notes/` built-in compliance reference notes used in RAG.
- `tests/Sylvaro.Tests` integration and unit tests for tenant isolation, RBAC enforcement, auth hardening, session lifecycle, API token lifecycle, action review workflows, and policy engine cache behavior.

## One command start

```bash
cp .env.example .env
docker compose up --build
```

After startup:

- Web UI: [http://localhost:8080](http://localhost:8080)
- API Swagger: [http://localhost:8081/swagger](http://localhost:8081/swagger)
- MinIO console: [http://localhost:9001](http://localhost:9001)

## Demo credentials (seeded)

- Tenant: `NordicFin AB`
- Email: `admin@nordicfin.example`
- Password: `ChangeMe123!`

## Happy path demo

1. Login in the web app with seeded credentials.
2. Open `AI Systems` and enter `LoanAssist`.
3. Add/update architecture components, data flows, data stores, and questionnaire answers.
4. Click `Run Assessment`.
5. Review generated findings/actions.
6. Add evidence excerpts and link them to actions/findings.
7. Review actions as `Approved` / `NeedsEdits` / `Rejected` through API or UI flow.
8. Configure Jira/Azure webhook stubs in `Tenant Settings` (optional).
9. Generate `DPIA_Draft` export as PDF or JSON (optionally send webhook).
10. Open `Audit Log` and `Evidence Gaps` to verify traceability.

## Local dev (without Docker)

```bash
dotnet restore
dotnet build Sylvaro.slnx
dotnet test Sylvaro.slnx
```

Run API:

```bash
dotnet run --project src/Sylvaro.Api/Sylvaro.Api.csproj
```

Run Web:

```bash
dotnet run --project src/Sylvaro.Web/Sylvaro.Web.csproj
```

## Key endpoints

- Auth: `/auth/login`, `/auth/register`, `/auth/refresh`, `/auth/logout`
- Tenant/users: `/tenants/me` (GET/PUT), `/tenants/roles`, `/tenants/users`
- AI systems/versions: `/aisystems`, `/aisystems/{id}/versions`
- Architecture: `/versions/{versionId}/architecture`, `/versions/{versionId}/architecture/components`, `/versions/{versionId}/architecture/flows`, `/versions/{versionId}/architecture/stores`
- Inventory: `/versions/{versionId}/inventory`
- Evidence: `/documents`, `/documents/upload`, `/documents/{id}/download`, `/documents/{id}/excerpts`, `/documents/evidence-links`
- Evidence map/gaps/search: `/versions/{versionId}/evidence/map`, `/versions/{versionId}/evidence/gaps`, `/versions/{versionId}/evidence/search`
- Assessments: `/versions/{versionId}/assessments/run`
- Assessment diff: `/versions/{versionId}/assessments/diff/{otherVersionId}`
- Findings: `/findings/assessment/{assessmentId}`
- Actions: `/actions/version/{versionId}`, `/actions/board/{versionId}`, `/actions/{actionId}/approve`, `/actions/{actionId}/reviews`
- Exports: `/exports/versions/{versionId}/generate`, `/exports/versions/{versionId}`, `/exports/{artifactId}/download`
- Integrations: `/integrations/webhooks`, `/integrations/webhooks/{provider}`
- Tenant policy packs: `/tenants/policy-packs`, `/tenants/policy-packs/{policyPackId}/enabled`
- Security: `/security/sessions`, `/security/sessions/revoke-others`, `/security/api-tokens`
- Audit: `/audit`, `/audit/export`

## Enterprise UI Theme

The platform now ships with a light institutional design system aimed at governance workflows:

- Warm off-white background with white and stone surfaces
- Bronze accent usage limited to interaction and emphasis
- Compact 12-column dashboard density for executive review
- Institutional error surfaces with correlation IDs and collapsible technical details
- Sticky table headers and dense row formatting for audit and registry views

Related files:

- `src/Sylvaro.Web/wwwroot/app.css`
- `src/Sylvaro.Web/Components/Layout/MainLayout.razor`
- `src/Sylvaro.Web/Components/Layout/NavMenu.razor`
- `src/Sylvaro.Web/Components/Shared/InstitutionalAlert.razor`
- `docs/sylvaro-design-system.md`

## Screenshot Checklist

Capture these pages after running Docker for visual QA and release notes:

1. `/login` (premium auth layout and brand panel)
2. `/` (Executive Overview with metric strip, compliance instrument, matrix, governance status)
3. `/ai-systems` (registry table with search and status filters)
4. `/audit-log` (table mode with action, actor, and date filters)
5. `/evidence-vault` (evidence inventory table)
## Troubleshooting

- If `docker compose up --build` fails with `Cannot connect to the Docker daemon`, start Docker Desktop and wait until it shows as running, then rerun compose.
- If the UI shows `Authentication required` after refresh, sign in again once to repopulate session storage. The app now auto-recovers access tokens and redirects to login only when refresh is no longer recoverable.

## Compliance-as-code and AI schemas

- Policy pack format: `docs/policy-pack-format.md`
- AI output JSON schemas: `docs/ai-json-schemas.md`
- Runbook: `docs/runbook.md`
