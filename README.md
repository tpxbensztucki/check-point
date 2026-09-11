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

## Getting started

### Run the whole stack (recommended)

Requires only Docker. From the repo root:

```bash
docker compose up --build
```

This starts Postgres, the API, and the frontend together:

- Frontend: http://localhost:8081
- API: http://localhost:8080 (health check at `/health`)
- Postgres: `localhost:5432` (user/password/db: `checkpoint`)

The API applies any pending EF Core migrations automatically on startup, so there's
no separate migration step. If ports 8080/8081/5432 are already in use on your
machine, override them in a `docker-compose.override.yml` (git-ignored) rather than
editing `docker-compose.yml` directly.

The environment variable names used here (`ConnectionStrings__Default`,
`API_BASE_URL`) are the same ones the deployed Azure Container Apps environment will
use, so config carries over without renaming anything.

### Backend only

Requires the .NET 10 SDK (or use the container image if it's not installed locally),
plus a Postgres instance reachable at the connection string in
`api/CheckPoint.Api/appsettings.Development.json` (defaults to `localhost:5432`,
matching `docker compose up db`):

```bash
cd api/CheckPoint.Api
dotnet run
```

Without a local SDK:

```bash
docker run --rm -v "$(pwd)/api":/src -w /src/CheckPoint.Api mcr.microsoft.com/dotnet/sdk:10.0 dotnet run
```

### Frontend only

```bash
cd web
npm install
npm run dev
```

## Status

Milestone 1 (Infrastructure & Tooling) in progress — containerisation and local
Docker Compose are done; CI/CD and Azure provisioning are still to come (Azure
provisioning is on hold pending Azure access).
