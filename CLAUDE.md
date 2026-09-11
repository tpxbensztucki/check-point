# Check Point — Client Feedback Tool

## What this is

Automates the collection of structured feedback on people within the practice, on a
schedule, from relevant points of contact (POCs), and routes it to the person's line
manager for review before it's shared with the individual. Removes the manual chasing
currently required to keep feedback flowing for new starters and for people on
long-running projects.

## Linear is the source of truth for work

**Always check Linear before starting or planning work here.** This repo has no local
backlog, roadmap, or ticket files — all of that lives in Linear.

- Workspace: `tpximpact`
- Team: **Cobalt** (key `CBLT`)
- Project: **Client Feedback Tool** — https://linear.app/tpximpact/project/client-feedback-tool-218df3856926
- Epics are tracked as **Milestones** on the project (e.g. "1. Infrastructure & Tooling",
  "5. Feedback Cycle Engine").
- Features are tracked as **labels** on issues, named `Feature: ...`.
- Each issue (story) includes Description, Acceptance Criteria, BDD (Given/When/Then)
  scenarios, and Notes/Assumptions where relevant — read the issue itself before
  implementing it, don't infer scope from the title alone.
- The project description mentions a `feedback-app-design-spec.md` as the source of
  truth for all epics/stories. As of this writing it is **not** attached to the Linear
  project as a document and does not exist in this repo — if you're picking up backlog
  work and can't find it, ask where it lives before assuming an issue's scope.

When implementing a backlog issue, use the Linear MCP tools to pull the actual issue
(description, acceptance criteria, BDD scenarios) rather than working from memory of
its title.

## Stack

- **Backend:** .NET 10, containerised — `api/CheckPoint.Api`
- **Frontend:** React + TypeScript + Tailwind CSS (via Vite) — `web/`
- **Database:** PostgreSQL via EF Core (`api/CheckPoint.Api/CheckPointDbContext.cs`),
  auto-applying pending migrations on API startup. Domain entities live in
  `api/CheckPoint.Api/Domain/` — `Person`/`Role` (many-to-many, Milestone 2) and
  `Department`/`Practice`/`Person` org fields (Milestone 3).
- **Hosting:** Azure Container Apps — **on hold**: provisioning (CBLT-205/206) is
  deferred until Azure access is available. Everything else in Milestone 1 that
  doesn't need Azure (containerisation, Docker Compose, later CI build/test) is not
  blocked by this.
- **Local dev:** `docker compose up --build` runs Postgres, API, and frontend
  together — see the root README.

## Repo layout

```
api/CheckPoint.Api/   .NET 10 Web API (Dockerfile, health check, EF Core + Postgres, no domain entities yet)
CheckPoint.slnx       .NET solution file
web/                  React + Tailwind frontend (Dockerfile, nginx, runtime-configurable API_BASE_URL)
docker-compose.yml    Full local stack: db + api + web
```

## Running locally

Prefer `docker compose up --build` from the repo root (see README) — it needs only
Docker, no local SDK/Node install. For running pieces individually:

```bash
# Backend — with a local SDK
cd api/CheckPoint.Api
dotnet run

# Backend — via Docker, no local SDK needed
docker run --rm -v "$(pwd)/api":/src -w /src/CheckPoint.Api mcr.microsoft.com/dotnet/sdk:10.0 dotnet run

# Frontend
cd web
npm install
npm run dev
```

Environment variable names are kept identical between local Docker Compose and the
(future) Azure deployment — `ConnectionStrings__Default` for the API, `API_BASE_URL`
for the frontend — so config is copy-paste-compatible between environments.

## Testing

- **Backend unit tests** — `api/CheckPoint.Api.UnitTests`. Pure logic, no HTTP, no
  database. Mirrors the folder structure of `api/CheckPoint.Api`. Run with:
  `dotnet test api/CheckPoint.Api.UnitTests`
- **Backend integration tests** — `api/CheckPoint.Api.IntegrationTests`. Boots the
  real API via `WebApplicationFactory<Program>` against a real Postgres instance
  started on demand with Testcontainers (`Testcontainers.PostgreSql`) — no manual
  database setup, but Docker must be running. Anything that needs the database, EF
  Core migrations, or a full HTTP round-trip belongs here, not in the unit test
  project. Run with: `dotnet test api/CheckPoint.Api.IntegrationTests`
- **Frontend unit tests** — colocated with the component/module they cover as
  `*.test.tsx` / `*.test.ts` next to the source file (e.g. `src/App.test.tsx` next to
  `src/App.tsx`). Uses Vitest + React Testing Library. Run with: `npm test` (in `web/`)
- Both `dotnet test` commands need a local .NET SDK, or run them via the
  `mcr.microsoft.com/dotnet/sdk:10.0` container the same way as other `dotnet`
  commands in this doc (mount the repo root, `-w /src`). The integration tests also
  need the Docker socket available to whatever runs them (Testcontainers starts and
  stops the Postgres container itself).
## CI

`.github/workflows/ci.yml` runs on every PR and on push to `main`: backend unit
tests, backend integration tests, frontend unit tests + build, and a build (no push)
of both Docker images — five separate jobs, each must pass. Pushing tagged images to
an Azure container registry and triggering a Container Apps deployment on merge to
`main` is **not** implemented yet — that depends on CBLT-205/206 (Azure provisioning,
on hold pending Azure access). Once Azure resources exist, add a job to this workflow
gated on `github.ref == 'refs/heads/main'` that logs in to ACR, re-runs the two image
builds with `push: true`, and triggers the deployment.

## Current state

Milestone 1 (Infrastructure & Tooling): containerisation (CBLT-203, CBLT-204),
Docker Compose (CBLT-207), test project scaffolding (CBLT-209), and PR/main CI
(CBLT-208, partial — see above) are done. Remaining: Azure image push + deploy (the
rest of CBLT-208) and Azure provisioning (CBLT-205/206), both on hold pending Azure
access.

Milestone 2 (Roles, Permissions & Auth): the Person↔Role data model (CBLT-210) and
the magic-link mechanism for guest respondents (CBLT-213 —
`api/CheckPoint.Api/Services/MagicLinkService.cs`) are done. AD SSO (CBLT-211) is not
yet implemented, and full RBAC enforcement per the Section 8 permission matrix
(CBLT-212) is **blocked** — the design spec that would define the actual matrix
isn't available (see below), and most of the actions it would gate don't exist as
endpoints yet. The magic-link mechanism is deliberately independent of the
`FeedbackRequest` entity (which doesn't exist yet — Milestone 5) and of the
guest-facing form itself (Milestone 6); it only knows an opaque `FeedbackRequestId`.

Milestone 3 (Org & People Management): Department and Practice creation (CBLT-214),
Person creation (CBLT-215), and editing a Person's details/Line Manager (CBLT-216)
are done, all Admin-only — see "Auth (interim)" below for how "who is calling" is
resolved ahead of real SSO. `Person` now carries `Status` (defaults to `Employed`),
a required `Practice`, and optional self-referencing `LineManager`/`HeadOfPractice`
links; a new Person is always created with no Roles (role assignment is a separate
story). `PUT /people/{id}` rejects a Person being set as their own Line Manager;
recalculating the orphaned-person flag on Line Manager change is deferred until
that flag exists (CBLT-219).

### Auth (interim, until CBLT-211)

There is no real sign-in yet. `api/CheckPoint.Api/Auth/DevPersonAuthenticationHandler.cs`
is a stand-in: the caller identifies themselves via a `DevPersonId` header carrying
an existing `Person`'s `Id`, and the handler loads that Person's `Role`s from the
database to build the ASP.NET Core role claims `[Authorize(Roles = ...)]` checks
against. This means permissions are already DB-driven (via the Person↔Role model
from CBLT-210) — when CBLT-211 replaces this with real AD SSO, only the *identity*
resolution changes (validating an AAD token instead of a header, then looking up the
matching Person), not the underlying role/claims model.

This scheme is registered only outside the `Production` environment (see
`Program.cs`) — in `Production`, no scheme is registered at all, so every
`[Authorize]`-protected endpoint rejects every request until real SSO exists
(fails closed rather than granting access). Local Docker Compose sets
`ASPNETCORE_ENVIRONMENT=Development` on the `api` service specifically so this
stand-in works for local testing; never set that in a real deployment.

To call a protected endpoint locally, pass an existing Person's id:
```bash
curl -X POST http://localhost:8080/departments \
  -H "Content-Type: application/json" \
  -H "DevPersonId: <an-admin-persons-guid>" \
  -d '{"name":"Tech & Data"}'
```
