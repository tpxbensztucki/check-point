# Check Point — Client Feedback Tool

Automates the collection of structured feedback on people within the practice, on a
schedule, from relevant points of contact, and routes it to the person's line manager
for review before it's shared with the individual.

Tracked in Linear: [Client Feedback Tool](https://linear.app/tpximpact/project/client-feedback-tool-218df3856926)

## Stack

- **Backend:** .NET 10 (`api/CheckPoint.Api`)
- **Frontend:** React + Tailwind CSS via Vite (`web/`)
- **Database:** PostgreSQL, EF Core migrations
- **Hosting:** Azure Container Apps
- **Local dev:** Docker Compose (not yet set up — see backlog)

## Getting started

### Backend

Requires the .NET 10 SDK (or use the container image if it's not installed locally):

```bash
cd api/CheckPoint.Api
dotnet run
```

Without a local SDK:

```bash
docker run --rm -v "$(pwd)/api":/src -w /src/CheckPoint.Api mcr.microsoft.com/dotnet/sdk:10.0 dotnet run
```

### Frontend

```bash
cd web
npm install
npm run dev
```

## Status

This repo currently contains only the initial scaffold. See the Linear project's
Milestone 1 (Infrastructure & Tooling) for the next steps: Dockerfiles, Docker
Compose, CI/CD, and Azure provisioning.
