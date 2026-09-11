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
- **Database:** PostgreSQL (Azure Database for PostgreSQL Flexible Server), EF Core
  migrations — not yet wired up
- **Hosting:** Azure Container Apps — not yet provisioned
- **Local dev:** intended to be Docker Compose mirroring deployed images — not yet
  set up (see Linear milestone "1. Infrastructure & Tooling")

## Repo layout

```
api/CheckPoint.Api/   .NET 10 Web API project (minimal scaffold, no domain logic yet)
CheckPoint.slnx       .NET solution file
web/                  React + Tailwind frontend (Vite scaffold, placeholder page only)
```

## Running locally

The `dotnet` SDK is not assumed to be installed locally — this project was scaffolded
and built using the `mcr.microsoft.com/dotnet/sdk:10.0` Docker image. Use whichever of
these you have available:

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

## Current state

This repo currently contains only the initial scaffold (empty API + empty React app,
both building successfully). No Dockerfiles, Docker Compose, CI/CD, Azure resources,
EF Core/Postgres wiring, or feedback-tool domain logic exist yet — those are all
upcoming Linear issues, starting with milestone "1. Infrastructure & Tooling"
(CBLT-203 through CBLT-209).
