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
- **Database:** PostgreSQL. EF Core is wired up with a bare `CheckPointDbContext`
  (no domain entities yet) and an initial migration; it auto-applies pending
  migrations on API startup.
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
- None of these are wired into CI yet — that's CBLT-208.

## Current state

Milestone 1 (Infrastructure & Tooling): containerisation (CBLT-203, CBLT-204),
Docker Compose (CBLT-207), and test project scaffolding (CBLT-209) are done.
Remaining: CI/CD (CBLT-208 — build/test doesn't need Azure but pushing images does)
and Azure provisioning (CBLT-205/206, on hold pending Azure access). No
feedback-tool domain logic exists yet — that starts at Milestone 2.
