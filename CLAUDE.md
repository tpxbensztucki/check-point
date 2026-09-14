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
  `Department`/`Practice`/`Person` org fields, including `Practice.PracticeLeadId`
  (Milestone 3), `Project`/`ProjectMembership`/`Poc` (Milestone 4), and
  `FeedbackRequest` (Milestone 5).
- **API structure — standing pattern for every feature area**: `Endpoints/` holds
  only routing (`MapGroup`/`RequireAuthorization` wiring, thin lambdas that bind a
  request, call one service method, and map the result to an `IResult`); request/
  response DTOs live in `Contracts/`, one file per feature area; `Services/` holds
  the actual business logic (validation, authorization checks that don't fit a
  plain `RequireRole` policy, EF Core queries), each method returning a small
  domain result type (a `...Status` enum plus a `...Result` record with static
  factory helpers — see `MagicLinkService`/`MagicLinkValidationResult` for the
  original precedent, or `PersonService`/`PersonResults.cs` for a fuller example).
  No `CheckPointDbContext` is injected directly into an endpoint lambda. This was
  introduced in CBLT-281 after `DepartmentEndpoints.cs`/`PersonEndpoints.cs` had
  accumulated real business logic inline across CBLT-214–219; apply it from the
  start for new endpoint groups rather than extracting it later. Deliberately not
  in scope: a repository/interface abstraction over `CheckPointDbContext` — it
  wouldn't add real unit-testability here (rules still need the database via EF
  either way) and would be premature.
- **Hosting:** Azure Container Apps — **on hold**: provisioning (CBLT-205/206) is
  deferred until Azure access is available. Everything else in Milestone 1 that
  doesn't need Azure (containerisation, Docker Compose, later CI build/test) is not
  blocked by this.
- **Local dev:** `docker compose up --build` runs Postgres, API, and frontend
  together — see the root README.

## Repo layout

```
api/CheckPoint.Api/            .NET 10 Web API
  Domain/                        EF Core entities
  Contracts/                     Request/response DTOs, one file per feature area
  Services/                      Business logic + EF Core queries, one class per feature area
  Endpoints/                     Routing only — thin lambdas calling into Services
  Auth/                          DevPersonAuthenticationHandler (see "Auth (interim)" below)
  Migrations/                    EF Core migrations
CheckPoint.slnx                .NET solution file
web/                            React + Tailwind frontend (Dockerfile, nginx, runtime-configurable API_BASE_URL)
  src/pages/                      One component per route/screen
  src/api.ts                      fetch wrapper(s) calling the backend, using getApiBaseUrl()
  src/App.tsx                     react-router-dom route table
docker-compose.yml              Full local stack: db + api + web
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
  `src/App.tsx`). Uses Vitest + React Testing Library. Run with: `npm test` (in `web/`).
  No HTTP-mocking library (e.g. MSW) is set up — network calls are mocked directly
  with `vi.stubGlobal('fetch', vi.fn(...))` (see `GuestFeedbackPage.test.tsx`),
  `vi.unstubAllGlobals()` in an `afterEach`/`beforeEach` to reset between tests.
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
Person creation (CBLT-215), editing a Person's details/Line Manager (CBLT-216),
role assignment/removal (CBLT-217), marking a Person as Leaver (CBLT-218),
cross-practice visibility/orphan detection (CBLT-219), and the scoped org tree
view (CBLT-220) are done — see "Auth (interim)" below for how "who is calling" is
resolved ahead of
real SSO. Everything under `/people` is Admin-only except `POST
/people/{id}/leaver`, which a Line Manager may also call for their own reports
(checked manually in the handler against `Person.LineManagerId`, since it isn't a
plain role check). Cancelling outstanding feedback requests and excluding a Leaver
from future cycle enrolment are deferred until the `FeedbackRequest` entity and
cycle engine exist (Milestones 5/6); the transition is deliberately one-way (no
"un-leaver" action), per CBLT-218's acceptance criteria.

`GET /practices/{id}/people` (Admin, or the Practice Lead of that Practice — same
manual-check pattern as the Leaver endpoint) lists the People tagged to a Practice
with a computed `IsOrphaned` flag: true when a Person has no `LineManagerId`, or
their Line Manager's own `PracticeId` differs from theirs. It's computed fresh on
every read (not a stored column), so it can never go stale when either Person's
Practice or Line Manager changes later — no separate recalculation step needed.

**CBLT-303 (add-on, prerequisite for CBLT-235):** `Person.Email` is a new
**nullable** `string?` field, settable via the existing `POST /people` and `PUT
/people/{id}` endpoints. Deliberately not `required` — no real sign-in exists
yet to require or verify one, and making it required would have forced every
existing `new Person { ... }` test call site across the integration suite to
change for no functional benefit today. It becomes load-bearing once CBLT-211
(real AD SSO) and CBLT-235 (per-submission LM notification email, Milestone 7)
exist; until then, a null `Email` just means "nothing to send to yet."
Since the endpoint filters to one Practice before returning results, a Line
Manager tagged to a different Practice never appears in another Practice's list,
even though their report (tagged to that Practice) does, flagged Orphaned.

`GET /org-tree` (any authenticated caller — the three-role scoping happens inside
`OrgTreeService`, not a group-level policy) returns a forest of `OrgPersonNode`,
each with nested `Reports`. Visibility is a union of whatever the caller's roles
grant: Admin sees everyone; Practice Lead sees everyone tagged to a Practice they
lead; Line Manager sees themselves plus exactly their direct reports (not deeper).
A Person whose Line Manager falls outside the caller's visible set becomes a root
in the returned forest rather than being dropped — the same rule
`GetPracticePeopleForViewerAsync` uses. A caller holding none of the three roles
gets back an empty list, not a 403. The tree-building code also guards against a
manager cycle in the data (e.g. two edits leaving A → B → A) to avoid infinite
recursion. `Person` now carries
`Status` (defaults to `Employed`), a required `Practice`, and optional
self-referencing `LineManager`/`HeadOfPractice` links; a new Person is always
created with no Roles (role assignment is a separate story). `PUT /people/{id}`
rejects a Person being set as their own Line Manager; recalculating the
orphaned-person flag on Line Manager change is deferred until that flag exists
(CBLT-219).

Milestone 4 (Project & POC Management): creating a Project and adding/removing a
Person (CBLT-221), and completing a Project (CBLT-222), are done, Admin-only.
`Project` has a `Status` (defaults to `Active`); `Person`<->`Project` is an
explicit join entity, `ProjectMembership` (not an implicit many-to-many like
`Person`/`Role`), because a Person's per-Project feedback cycle (Milestone 5) will
need somewhere to attach state to a specific Person-Project pairing. `DELETE
/projects/{id}/people/{personId}` soft-deletes — sets `RemovedAt` rather than
deleting the row — so a Person's history on a Project survives removal, per this
story's acceptance criteria. Adding a Leaver to a Project is rejected; adding a
Person also now schedules their New Starter cycle (CBLT-226, see below).
`POST /projects/{id}/complete` performs the status transition (rejecting an
already-Completed Project) and cancels every still-`Scheduled` `FeedbackRequest`
tied to the Project (CBLT-226); completing a Project never touches the Person's
own status or their other Projects.

Assigning POCs (CBLT-223) is scoped to one Person's `ProjectMembership` (`Poc`
entity, cascade-deletes with its membership since it's meaningless without one).
`POST`/`GET /projects/{id}/people/{personId}/pocs` are callable by Admin, the
Practice Lead of the target Person's Practice, or the Line Manager of the target
Person — a three-way manual check in `PocService`, the same pattern as the Leaver
and Practice-view endpoints. `MissingStandardRoles` (which of Tech/DM/Other have no
active POC yet) is computed fresh on every read, never stored. `PUT`/`DELETE
.../pocs/{pocId}` (CBLT-224, same three-way authorization) edit or hard-delete a
single POC — no "history" requirement exists for POCs the way it does for
`ProjectMembership`, so removal is a real delete, not a soft one. Cancelling a
removed POC's outstanding feedback request is still deferred (removing a POC isn't
the same as completing a Project, and the dispatch job that would need to look
this up doesn't exist yet, Milestone 7); correcting a POC's email has no effect on
already-sent magic links since `MagicLink` only ever carries an opaque
`FeedbackRequestId`, never the POC's email — that acceptance criterion is already
satisfied structurally, no code needed for it.

`GET /people/{personId}/projects` (CBLT-225) lists every Project a Person is
currently on, with per-Active-Project `MissingStandardRoles` (`null` for a
Completed Project, since completeness stops being meaningful there). Visibility
follows the same role scoping as the org tree (Admin: all; Practice Lead: own
Practice; Line Manager: own reports) — the same manual-check pattern as Leaver and
Practice-view, living in `ProjectService.GetProjectsForPersonAsync`. The missing-
roles computation itself is shared with `PocService` via
`Domain/PocRoleHelpers.cs` rather than duplicated.

Milestone 5 (Feedback Cycle Engine): New Starter cycle scheduling (CBLT-226) is
done. The `FeedbackRequest` entity now exists — one row per scheduled request,
carrying `ProjectMembershipId`, `ScheduledFor`, and a `Status`
(`Scheduled`/`Sent`/`Cancelled`) — but it deliberately carries no snapshot of which
POCs to send to; the dispatch job (Milestone 7, not built yet) resolves the
Project's currently assigned POCs at send time, satisfying that acceptance
criterion by construction. `ProjectService.AddPersonAsync` schedules one
`FeedbackRequest` per configured interval (default 2/4/8 weeks) relative to the
Person's own `ProjectMembership.JoinedAt`, not the Project's creation date, so
staggered starters get staggered schedules. Interval config
(`NewStarterCycleOptions`, bound from the `NewStarterCycle` configuration section)
is an interim stand-in for CBLT-252 (Admin Settings: Configure New Starter
interval schedule) — changing it via config/env var already satisfies "not
hardcoded," it just isn't editable through the app itself yet; when CBLT-252
lands, only where the value is read from changes, not `AddPersonAsync`'s logic.
`ProjectService.CompleteProjectAsync` now also cancels every still-`Scheduled`
`FeedbackRequest` tied to the Project, closing out that part of CBLT-222's
originally-deferred scope. `MagicLink.FeedbackRequestId` remains a bare,
unconstrained `Guid` rather than a real FK to `FeedbackRequest` — that decoupling
was a deliberate CBLT-213 design choice and isn't revisited here.

`FeedbackRequest` also now carries a `Stage` (`FeedbackRequestStage`:
`NewStarterWeek2`/`Week4`/`Week6`/`Week8`), separate from `Status` — needed so the
cycle engine can recognise "the 4-week check-in" reliably rather than inferring it
from `ScheduledFor` minus `JoinedAt`, which would break under a reconfigured
interval set. `FeedbackCycleService.HandleCheckInFlaggedAsync` (CBLT-227) is the
auto-insert-a-6-week-check-in hook: flagging a `NewStarterWeek4` request schedules
one `NewStarterWeek6` request (idempotent — a second flag is a no-op), flagging
any other stage does nothing. **Not wired to any endpoint** — the unified flag
action itself doesn't exist yet (CBLT-239, Milestone 8; CBLT-230 is the ticket that
will call this hook from it) — so this is tested by calling the service directly,
same style as `ProjectServiceSchedulingTests`/`MagicLinkServiceTests`.

`ProjectMembership.GeneralCycleEnrolledAt` (CBLT-228, nullable, set once and never
cleared) marks a membership as having transitioned from the New Starter cycle into
the General (quarterly) cycle — it's both the idempotency guard and, for CBLT-229
(FY-quarter scheduling), the anchor date that cycle will count from.
`FeedbackCycleService.HandleFeedbackRequestCompletedAsync` sets it when the
`NewStarterWeek8` request (always the last New Starter stage chronologically,
whether or not a `Week6` was inserted) concludes — unless the Project has since
Completed or the Person has since become a Leaver, in which case enrolment is
skipped. Per Person per Project by construction, since it only ever touches the
one membership tied to the completed request. Like CBLT-227's hook, **not wired to
any endpoint** — neither trigger (guest submission, Milestone 6; No Response
expiry, CBLT-237) exists yet — so it's tested by calling the service directly.

`FeedbackCycleService.HandleFeedbackRequestCompletedAsync` (CBLT-229) now
dispatches by `Stage`: a `NewStarterWeek8` completion enrols into the General
cycle (CBLT-228) *and* schedules its first `FeedbackRequest.Stage.General`
request; a `General` completion schedules the next one, continuing every FY
quarter until the Project completes or the Person becomes a Leaver. FY quarters
run Apr–Jun/Jul–Sep/Oct–Dec/Jan–Mar (spec Section 5.2), so boundaries are always
the 1st of Jan/Apr/Jul/Oct. On first enrolment, if the next boundary falls within
the configured skip threshold (`GeneralCycleOptions.SkipThresholdWeeks`, default
4, interim stand-in for CBLT-253 exactly like `NewStarterCycleOptions`/CBLT-252)
of the enrolment moment, that quarter is skipped in favour of the one after.
Subsequent quarters are anchored to the *previous* request's own `ScheduledFor`
(always a quarter-start date) rather than "now" processed-at time, so the cadence
never drifts. A single `FeedbackRequestStage.General` value covers every quarter —
unlike the New Starter stages, quarters have no distinct identity beyond "the next
one." Idempotency for both the first and subsequent schedules is a
does-a-later-request-already-exist check, same shape as CBLT-227's guard.

`FeedbackCycleService.HandleCheckInFlaggedAsync` (CBLT-230) is now the single,
well-defined "a check-in's feedback was flagged" entry point — the same method
CBLT-227 introduced, expanded to always set `Person.UnderReviewSince` (orthogonal
to `Status`: Employed/Leaver is an employment lifecycle, being under review is a
separate, overlapping flag, spec Section 5.3) and create a pending `CatchUp`
record, with the New-Starter-4-week-specific 6-week insert layered on top only for
that stage. Idempotent per check-in via a does-a-`CatchUp`-already-exist-for-this-
`FeedbackRequestId` check, so a second flag on the same request does nothing (but
a *different* check-in for the same Person still gets its own `CatchUp`). `CatchUp`
doesn't store who the LM/Practice Lead actually are — like `Person.IsOrphaned`,
that's resolved live via `Person.LineManagerId`/`Practice.PracticeLeadId` at read
time. Recording a catch-up's outcome (Milestone 8, CBLT-241) isn't implemented
here — only the `Pending` state exists so far. Now wired to `POST
/feedback-requests/{id}/flag` via `FeedbackCycleService.FlagCheckInAsync`
(CBLT-239, Milestone 8) — see that section below.

Milestone 6 (Guest Feedback Form): the guest landing page (CBLT-231) is done — the
**first real frontend UI screen** in this project (everything before it was
backend-only). `GET /magic-links/{token}` (`MagicLinkEndpoints.cs`) wraps the
existing `MagicLinkService.ValidateAsync` and is deliberately unauthenticated — no
`RequireAuthorization` at all — since a guest never signs in (spec Section 9); it
maps `Valid`→200, `NotFound`→404, `Expired`→410, `AlreadyUsed`→409. `react-router-
dom` was added as the frontend's first routing dependency (none existed before);
`web/src/App.tsx` is now the route table, `web/src/pages/` holds one component per
screen, and `web/src/api.ts` is the fetch-wrapper convention for calling the
backend (no HTTP client library added — plain `fetch` plus `getApiBaseUrl()`).
`GuestFeedbackPage` renders one of: loading, expired, already-submitted, invalid-
link, or (once the link is `Valid`) the feedback form. `web/nginx.conf` already had
a SPA fallback (`try_files $uri /index.html`) from the original scaffold, so no
changes were needed there for client-side routing to work in the container.

`web/src/components/FeedbackForm.tsx` (CBLT-232) is the three-field form itself —
"What they are doing well" / "What they aren't doing well" / "What they need to
improve" — all required, each capped at 2000 characters
(`FEEDBACK_FIELD_MAX_LENGTH`). Deliberately does **not** set an HTML `maxLength` on
the `<textarea>`s: a guest can type past the limit, see the counter turn red, and
get a blocking validation message on submit, rather than being silently stopped
mid-keystroke — this matches the ticket's own BDD scenario (entering 2050
characters, then being blocked) and gives clearer feedback than a hard cap. The
Submit button is never `disabled`; invalid submission attempts show inline
`role="alert"` errors and `aria-invalid`/`aria-describedby` on the offending
field(s) instead, since a silently-disabled button is a common accessibility
pitfall (spec Section 14) — screen-reader/keyboard users get no explanation for
why nothing happens. `FeedbackForm` only validates and calls its `onSubmit` prop; `GuestFeedbackPage`
wires the real submission call (see below). Added `@testing-library/user-event`
as a new dev dependency for its tests (real typing/click/blur simulation, not
just `fireEvent`).

`POST /magic-links/{token}/submission` (CBLT-233, `FeedbackSubmissionService`)
handles a completed submission. Content validation (required + 2000-char limit,
mirroring `FeedbackForm`'s own rules since the API is public and can't trust
client-side validation alone) happens *before* the magic link is touched, so a
rejected submission never burns the guest's one chance to submit — only once
validation passes does the service call `MagicLinkService.LoadAndCheckAsync`
(now `internal`, not `private`, precisely so this service can validate a token
and then mutate the same tracked `MagicLink` itself as part of one
`SaveChangesAsync`, rather than calling `ConsumeAsync` separately and
potentially consuming the link before knowing the submission will succeed). One
`SaveChangesAsync` call marks the `MagicLink` used, inserts the immutable
`FeedbackSubmission` row, and — if the Person has a `LineManagerId` set —
inserts an `LmNotification` outbox row. `FeedbackRequest.Status` is deliberately
left untouched by submission: it tracks the request's own dispatch lifecycle
(`Scheduled`/`Sent`/`Cancelled`, `Sent` meaning "the request email was
dispatched" — see CBLT-302 below), not response state, since a request can have
several currently-assigned POCs each responding independently. There is
deliberately no endpoint that edits a `FeedbackSubmission`: immutability (spec
Section 8/10) is enforced by omission, not a guarded field. `LmNotification`
is a durable outbox, not an actual send: real delivery is the Notifications
epic's dispatch job (not yet built), but because the row is written in the same
transaction as the submission it satisfies the ticket's "must not be silently
dropped, even if the mechanism temporarily fails" requirement by construction —
there's nothing to lose since nothing is attempted synchronously yet. No line
manager assigned is treated as "nobody to notify" rather than a dropped
notification. The frontend's `submitFeedback` (`web/src/api.ts`) posts the
guest's `FeedbackFormValues` as JSON to this endpoint; `GuestFeedbackPage` shows
the "Thank you" confirmation on success, or an inline `role="alert"` message
(keeping the form on screen) for an expired/already-used link or any other
failure, guarding against a double-submit firing a second request while the
first is still in flight.

**CBLT-302 (bug fix, found while scoping CBLT-234):** `MagicLink` and
`FeedbackSubmission` were both missing a `PocId` reference — CBLT-233's own AC
required saving feedback "against the correct request, POC, Person, and
Project," but no POC-to-magic-link relationship existed at all, since CBLT-231
and CBLT-233's tests only ever issued links against an arbitrary
`FeedbackRequestId`. This became unavoidable once CBLT-234 (per-POC email
dispatch) needed to issue one magic link per currently-assigned POC, not one
per request — and CBLT-237's own AC confirms outcomes are tracked **per POC**,
not aggregated onto the request as a whole (`"a request with 3 POCs can have a
mix of Submitted and No Response outcomes"`). Fixed by adding `PocId` (real FK
to `Poc`) to both `MagicLink` and `FeedbackSubmission`, and changing
`FeedbackSubmissions`' unique index from `FeedbackRequestId` alone to
`(FeedbackRequestId, PocId)`, so each POC can submit independently for the same
request but not twice. `MagicLinkService.IssueAsync` now takes a `pocId`
parameter. Whether a given POC has responded is always answered by whether a
`FeedbackSubmission` row exists for that `(FeedbackRequestId, PocId)` pair —
never stored as a status on `FeedbackRequest` itself.

Milestone 7 (Notifications & Response Tracking): CBLT-234 (`RequestDispatchService`)
sends the POC feedback request email containing a magic link. One `MagicLink` (and
one email) per currently-assigned POC on the request's `ProjectMembership` — each
scoped to that POC and that request only, per CBLT-302's fix. Entry points:
`DispatchDueAutomaticRequestsAsync` (the Automatic-mode driver, polled every minute
by `RequestDispatchBackgroundService`, an `IHostedService`), `DispatchManuallyAsync`
(the authorised-user trigger, `POST /feedback-requests/{id}/dispatch`, same
Admin-or-LM-or-PracticeLead scoping as `PocService`), and `SendReminderAsync`
(CBLT-236, below). The global mode is
`RequestDispatchOptions.Mode` (`Automatic`/`Manual`, config-bound, interim until
CBLT-254's Admin Settings toggle exists) — the background job only sends when
`Automatic`; the manual endpoint works regardless of mode, since an authorised user
can always force a send. `RequestDispatchBackgroundService` is **not registered in
the "Testing" environment** (see `Program.cs`): it runs on real wall-clock time via
`Task.Delay`, which would otherwise fire unpredictably against tests that advance a
`FakeTimeProvider` instead of real time.

Actual email sending is behind a new `IEmailSender` interface — `SmtpEmailSender` is
the real implementation (BCL `SmtpClient`, configured via `SmtpOptions`; no SMTP
server exists in any environment this project has run in yet, so `SmtpOptions.Host`
defaults empty and a misconfigured deployment fails loudly rather than pretending to
send). Tests substitute a `RecordingEmailSender` fake (same reasoning as
`FakeTimeProvider`). The frontend URL embedded in each email comes from a new
`FrontendOptions.BaseUrl` (config-bound, plain infra — not tied to any Admin Settings
ticket), wired via `Frontend__BaseUrl` in `docker-compose.yml`.

A due request whose `ProjectMembership.RemovedAt` is set (the person left the
project after the request was scheduled) is marked `Cancelled` instead of sent,
mirroring `ProjectService.CompleteProjectAsync`'s existing cancel-on-completion
behaviour for the narrower per-Person case. A due request with zero currently
assigned POCs is left `Scheduled` and retried on the next automatic pass rather
than being marked `Sent` with nothing actually sent.

CBLT-236 (`RequestDispatchService.SendReminderAsync`, `POST
/feedback-requests/{id}/pocs/{pocId}/remind`) resends to one non-responding POC —
a plain resend, not a new `FeedbackRequest`: issues a fresh `MagicLink` (fresh
7-day expiry) and invalidates whatever prior, still-usable link(s) existed for
that exact `(FeedbackRequest, Poc)` pair, so the old one stops working. This
needed a new `MagicLink.InvalidatedAt` field, distinct from `UsedAt` — a
superseded link was never used to submit, it was just replaced. `MagicLinkService`
and `FeedbackSubmissionService` both gained a `Superseded` status (mapped to `410
Gone`, same as `Expired`, on both the link-view and submission endpoints) so a
guest who still has an old link sees "replaced by a more recent one" rather than
the misleading "already submitted." Rejected with `NotYetDispatched` if the
request isn't `Sent` yet, or `AlreadySubmitted` if a `FeedbackSubmission` already
exists for that POC — matching CBLT-237's per-POC (not per-request) response
model. Scoped down: the two UI surfaces the ticket names (a Person's detail view,
the Admin/Practice Lead Dashboard) don't exist yet (Milestone 9) — this ships the
backend capability (the endpoint) only; wiring a reminder button into either view
is for whoever builds those screens.

CBLT-237 (`RequestDispatchService.GetPocStatusesAsync`, `GET
/feedback-requests/{id}/pocs`) reports each currently-assigned POC's outcome for a
request — `NotYetSent` / `Sent` / `Submitted` / `NoResponse` / `Cancelled` — never
a single status on the request as a whole, since different POCs on the same
request can be in different states (its own AC gives the example: one Submitted,
two No Response). Deliberately **computed live** on every call rather than a
stored flag flipped by a background job: given the current time, whether a
`FeedbackSubmission` exists for that `(FeedbackRequestId, PocId)` pair, and the
most recent non-invalidated `MagicLink` for that pair, the correct status follows
directly with no risk of drifting out of sync with "now" the way a periodically-
run job could (and no new job/infra needed). A POC added to the project after the
request was already dispatched (no link was ever issued to them) reads as
`NotYetSent`, same as before dispatch.

`Contracts/FeedbackRequestContracts.cs` holds the new `PocResponseStatus` enum and
`PocResponseStatusEntry` response record — the first contracts file for
`FeedbackRequest`-shaped responses (previously `FeedbackRequest` had no view
endpoint of its own, only the dispatch/reminder actions).

CBLT-235 (`LmNotificationDispatchService.DispatchPendingNotificationsAsync`,
polled every minute by `LmNotificationDispatchBackgroundService` — same
not-registered-in-`"Testing"` pattern as `RequestDispatchBackgroundService`)
delivers the `LmNotification` outbox CBLT-233 already queues one row per
submission for, never batched — three respondents submitting at different times
means three separate emails to the LM, not one combined notification, per this
ticket's own AC. A pending row whose `LineManager.Email` is null is skipped
(left pending, retried next pass) rather than treated as a failure — same
reasoning as "no Line Manager assigned" elsewhere. The email contains a link,
never the feedback content itself (spec Section 7's sensitivity requirement);
the link points at `{FrontendOptions.BaseUrl}/people/{personId}`, a route that
**does not exist in the frontend yet** — no Person-detail view is built until
Milestone 9. No magic-link-style token is needed for this link (unlike the
guest flow): an LM is a standing system user, so once that page exists it's
protected by ordinary `[Authorize]`, not a one-time link.

CBLT-238 (`PocResponseHistoryService`) closes out Milestone 7 — tracks
non-response as a pattern across check-ins, not just the single most recent
one. `GetPocHistoryAsync` (`GET /pocs/{pocId}/response-history`, single-target
gate like `PocService`) generalizes CBLT-237's per-(request, POC) status
computation across every `FeedbackRequest` sharing that POC's
`ProjectMembershipId`, ordered most-recent-first: a request the POC was never
actually dispatched to (no `MagicLink` ever issued to them for it — e.g. added
to the membership after that request fired) is excluded from their history
entirely rather than counted as anything. Returns both
`ConsecutiveNoResponseCount` (from the most recent entry backwards, stopping at
the first non-`NoResponse`) and `TotalNoResponseCount` — no fixed "pattern"
threshold is invented, since neither the ticket's AC nor the spec defines one;
the raw counts are returned, same live-computed-not-stored philosophy as
CBLT-237. `GetProjectPocPatternsAsync` (`GET
/projects/{projectId}/poc-response-patterns`) is a **filtered list**, not a
pass/fail gate — it follows `OrgTreeService.GetOrgTreeForViewerAsync`'s exact
shape (a `visiblePersonIds` union: Admin sees everyone, a Practice Lead sees
Pocs under People in practices they lead, a Line Manager sees Pocs under
themselves + direct reports only), since different Pocs on the same Project
can belong to People the caller can and can't see — an LM with no reports on
that Project simply gets an empty list back, not `403`.

This ticket also extracted `PersonAuthorizationHelpers.IsAuthorizedForPersonAsync`
(Admin, or the target Person's own Line Manager, or their Practice's Lead) out
of `PocService` and `RequestDispatchService`, which had been carrying
byte-for-byte identical copies of this check — the same "extract once genuinely
reused a third time" precedent as `PocRoleHelpers.ComputeMissingRoles` (CBLT-225).

Milestone 8 (Ad-hoc Review / Flagging): CBLT-239 (`FeedbackCycleService.FlagCheckInAsync`,
`POST /feedback-requests/{id}/flag`) is the first ticket to actually wire up
`HandleCheckInFlaggedAsync` (CBLT-227/230), which had sat dangling with zero
non-test callers since Milestone 5. `FlagCheckInAsync` is a thin caller-aware
wrapper: loads the `FeedbackRequest`, authorizes, then calls the existing hook
unchanged and returns the resulting `CatchUp`. Deliberately a **two-way**
check (Admin OR the Person's own Line Manager) — same shape as
`PersonService.MarkAsLeaverForViewerAsync` — rather than the three-way
`PersonAuthorizationHelpers` every other Milestone 7 endpoint uses: CBLT-239's
own AC only ever mentions a Line Manager ("A Line Manager cannot flag feedback
for a Person who is not their report"), unlike CBLT-240's ad-hoc trigger
(next), which explicitly includes Practice Lead too — a deliberate,
textually-supported contrast between the two tickets, not an oversight.
`Contracts/CatchUpContracts.cs` (new) holds `CatchUpResponse` — the first
contract for `CatchUp` itself, since no endpoint had ever touched it before.

CBLT-240 (`FeedbackCycleService.TriggerAdHocReviewAsync`, `POST
/people/{personId}/ad-hoc-review`) lets a Practice Lead or Line Manager start a
review at any time, independent of a check-in — same `CreateCatchUp`
mechanism as flagging (extracted as a small private helper: set
`UnderReviewSince`, add a `CatchUp`), but with `FeedbackRequestId` left `null`
and never triggering a 6-week insert. Three-way auth via
`PersonAuthorizationHelpers`, unlike CBLT-239's two-way check, since this
ticket's own AC explicitly names both roles. `CatchUp.FeedbackRequestId`
became **nullable** for this (a real schema change — Milestone 5 only ever
anticipated check-in-triggered catch-ups; no endpoint had touched `CatchUp` at
all before CBLT-239, so this isn't a fix to shipped behaviour, just anticipated
evolution). Guards independently of `HandleCheckInFlaggedAsync`'s own
per-`FeedbackRequestId` idempotency check: `TriggerAdHocReviewAsync` looks for
**any** `Pending` `CatchUp` for the Person (from either path) and surfaces it
instead of creating a duplicate if one exists — but this is *this method's
own* rule, not a change to flagging's behaviour. **Important correction from
the original Milestone-5-era assumption**: flagging two *different* check-ins
for the same Person still creates two separate `CatchUp` rows (proven by an
already-passing test, `CatchUpHookTests.FlaggingDifferentCheckInsForTheSamePerson_CreatesASeparateCatchUpEach`)
— `HandleCheckInFlaggedAsync` was deliberately left untouched rather than
widening its guard to match the ad-hoc path's, which would have broken that.
An ad-hoc `CatchUp` already pending for a Person never suppresses a later
4-week check-in's 6-week insert, since the two guards are entirely
independent of each other.

CBLT-241 (`CatchUpService.RecordOutcomeAsync`, `POST
/catch-ups/{catchUpId}/outcome`) is the first ticket where "once a `CatchUp`
already exists" concerns get their own service — distinct enough from
`FeedbackCycleService`'s flag/ad-hoc-trigger mechanics to warrant a split, same
reasoning as `RequestDispatchService`/`PocResponseHistoryService` splitting off
in Milestone 7. `CatchUpOutcomeType` (new enum: `SixWeekCheckInAdded`,
`NoActionClosed`, `EscalateFurther`, `Other`) plus new `CatchUp.OutcomeNotes`/
`RecordedAt` fields — the ticket's own "or free-text equivalent" AC is
satisfied by pairing `Other` with a required `OutcomeNotes`, rather than
adding unlimited freeform categories. Three-way auth (matching CBLT-240, not
CBLT-239). Recording an outcome clears `Person.UnderReviewSince` **unless**
the outcome is `EscalateFurther` — the review isn't actually over yet in that
case, per the ticket's own AC. Rejects with `AlreadyRecorded` if the
`CatchUp`'s `Status` isn't still `Pending`, which — combined with
`TriggerAdHocReviewAsync`'s own "any Pending catch-up" check from CBLT-240 —
means a Person whose catch-up was just resolved can immediately have a fresh
one opened by a new flag or ad-hoc trigger, exactly matching this ticket's
third BDD scenario. `Contracts/CatchUpContracts.cs`'s `CatchUpResponse` grew a
`From(CatchUp)` static factory once two services needed to build the same
response shape.

CBLT-242 (`CatchUpService.GetHistoryAsync`, `GET /people/{personId}/catch-ups`)
closes out Milestone 8 — a Person's full flag/ad-hoc-review/catch-up-outcome
history in one place, ordered most-recent-first, same three-way scoping and
"empty list is a normal Success, not an error" precedent as
`PocResponseHistoryService.GetPocHistoryAsync` (Milestone 7). Returns
`PersonCatchUpHistoryResponse` — `UnderReviewSince` surfaced at the top level
(not just inferred from the entries) so a currently-active review is
trivially distinguishable from resolved history, per the ticket's own AC.
`CatchUpResponse` gained a computed `TriggerSource` (`CheckIn`/`AdHoc`, derived
from whether `FeedbackRequestId` is set) so consumers don't have to re-derive
it themselves — the ticket's own AC calls out "trigger source" as a field the
history view must show.

Milestone 9 (Admin/Practice Lead Dashboard): **CBLT-304 (prerequisite, not a
spec ticket)** builds the dashboard shell and a dev-only sign-in stand-in —
none of CBLT-243/244/245 could land without somewhere to attach their
sections, and the frontend had no concept of "who is signed in" at all before
this (everything before it was either backend-only or the unauthenticated
guest flow). New `GET /dev/people` (`Endpoints/DevEndpoints.cs`,
`DevPersonDirectoryService`) is deliberately unauthenticated — it exists so a
sign-in picker has something to search *before* the viewer has any identity,
the same bootstrapping problem `DevPersonAuthenticationHandler` itself never
had to solve (a curl caller already knows a Person's id). Registered only
outside `Production`, the same gate as the dev auth scheme itself, and both
will be deleted together once CBLT-211 (real AD SSO) lands. On the frontend,
`web/src/auth/currentPerson.ts` is a small localStorage-backed accessor (not
a React context) holding `{ id, fullName, roles }`; `api.ts`'s new
`authorizedFetch` attaches it as the `DevPersonId` header on every dashboard
call, while the existing guest-facing functions stay untouched and
unauthenticated. `SignInPage` is a searchable person-switcher (deliberately
not a single fixed identity) so Admin/Practice Lead/Line Manager scoping can
actually be exercised and compared in the browser — the entire point of a
switcher over a hardcoded id. `DashboardLayout` + `RequireCurrentPerson` are
the shell and route guard every dashboard screen (this milestone's three
real tickets) mounts inside; the placeholder `HomePage` is gone, replaced by
`/` redirecting to `/dashboard`. The three dashboard sections render a
`ComingSoonPage` placeholder until CBLT-243/244/245 replace them one at a
time, each in its own PR.

CBLT-245 (`OrgTreePage` at `/dashboard/org-tree`) replaces that section's
`ComingSoonPage` placeholder from CBLT-304 — the first of Milestone 9's three
real ticketed screens. No backend change: `GET /org-tree` (CBLT-220) was
already fully role-scoped, so this is purely frontend wiring, satisfying the
ticket's own "no duplicate tree implementation" AC. `OrgTreeNode` recurses
over `Reports` to render the forest at arbitrary depth.

CBLT-243 (`OutstandingRequestsPage` at `/dashboard/outstanding-requests`,
`GET /dashboard/outstanding-requests`, `DashboardService`) is the first
Milestone 9 ticket needing a genuinely new aggregate backend query — nothing
before this listed outstanding requests across more than one
Person/POC/Project at a time. `PersonAuthorizationHelpers` gained
`GetVisiblePersonIdsAsync` — the union-of-visible-Person-ids scoping
(Admin: no filter; Practice Lead: own practice; Line Manager: self + direct
reports) had already been duplicated once between `OrgTreeService` and
`PocResponseHistoryService.GetProjectPocPatternsAsync`; this ticket's own
need made it a third occurrence, this project's established threshold for
extracting a shared helper (same reasoning as `PocRoleHelpers`). Both
existing call sites were refactored onto it in this same PR, mechanically,
with no behavior change (their own tests still pass unmodified).
`RequestDispatchService.ComputePocStatus` was made `internal` (from
`private`) so `DashboardService.GetOutstandingRequestsAsync` reuses the exact
same live-computed per-(request, POC) status logic
`RequestDispatchService.GetPocStatusesAsync` already used for a single
request, batched here across every request the caller can see. "Outstanding"
means the computed status isn't `Submitted` (`NotYetSent`/`Sent`/`NoResponse`)
— the ticket's own two example statuses. Grouping "by cycle" (its third AC)
is a frontend concern: every `OutstandingRequestEntry` already carries
`Stage`, so `OutstandingRequestsPage` groups client-side rather than adding a
per-stage backend endpoint, the same "compute/derive, don't pre-slice"
precedent as `CatchUpResponse.TriggerSource`. The manual reminder action
reuses the existing `POST /feedback-requests/{id}/pocs/{pocId}/remind`
(CBLT-236) — first called from the frontend here.

CBLT-244 (`FlaggedPeoplePage` at `/dashboard/flagged-people`, `GET
/dashboard/flagged-people`, `DashboardService.GetFlaggedPeopleAsync`) closes
out Milestone 9. Deliberately keyed off "has a `Pending` `CatchUp`", not
`Person.UnderReviewSince != null` — the ticket's own title and first AC say
"Under Review **with a pending catch-up**", and the two can diverge: after an
`EscalateFurther` outcome (CBLT-241), `UnderReviewSince` is deliberately left
set but that `CatchUp`'s own `Status` becomes `Recorded`, so the Person
correctly stops appearing in this view until a fresh flag or ad-hoc trigger
opens a new `CatchUp` — confirmed by its own test,
`APersonEscalatedFurther_DoesNotAppearUntilAFreshCatchUpExists`. Uses the
same `GetVisiblePersonIdsAsync` scoping as CBLT-243.

This PR also builds `CatchUpOutcomePage` (`/dashboard/people/{personId}/catch-up`)
— the "catch-up outcome recording view" CBLT-244's own third AC/BDD scenario
says selecting a flagged Person must navigate to, which didn't exist yet:
CBLT-241/242 shipped backend-only in Milestone 8 (`POST
/catch-ups/{id}/outcome`, `GET /people/{id}/catch-ups`), explicitly deferred
at the time since Milestone 9 didn't exist. This is that frontend, arriving
because CBLT-244's own wording requires the navigation target to work, not
scope creep. It shows the Person's full catch-up history, with a form (only
for the currently `Pending` entry) mirroring the backend's own validation —
`Other` requires notes, shown as an inline `role="alert"` message rather than
a disabled submit button, the same accessibility precedent `FeedbackForm`
established in Milestone 6.

`POST /people/{id}/roles` and `DELETE /people/{id}/roles/{roleName}` assign/remove
one of the fixed Role names on a Person. Assigning `Practice Lead` requires a
`PracticeId` and sets that `Practice`'s `PracticeLeadId`; removing the role clears
`PracticeLeadId` on every Practice the Person leads. Re-assigning a role a Person
already holds (e.g. to change which Practice they lead) is rejected — that's left
for a future story, since neither the spec nor the current backlog covers it.

## Milestone 13 (Admin Console)

New milestone, added once the dashboard was actually clicked through in a
browser: there is no frontend anywhere for creating or editing a Department,
Practice, Person, Project, or POC — all of it has been backend-API-only
since Milestones 3/4. This is permanent, essential functionality, not
throwaway test tooling — AD SSO (CBLT-211) will authenticate people, but it
will never supply org/people/project data, so this application always has
to be where that data is entered and maintained.

CBLT-305 (`DepartmentsPage` at `/dashboard/admin/departments`) is the first
ticket — org structure management. New `GET /departments`
(`DepartmentService.GetAllAsync`, Admin-only) returns every Department with
its nested Practices via `DepartmentWithPracticesResponse` — the first
browse view over the org hierarchy; every existing `DepartmentService`
method before this was either a create or a Practice-scoped, role-gated
read. No edit/delete for either Department or Practice — the backend has no
update/delete endpoint for either today, and this ticket doesn't introduce
one. `DashboardLayout`'s nav gained an "Admin" section, shown only when the
signed-in person holds the `Admin` role (a client-side UX nicety — the real
enforcement stays server-side, unchanged) — CBLT-306/307 will add their own
links to it.

### CORS (bug fix, found while testing the dev seed data end-to-end in a browser)

There was no CORS configuration anywhere in the API — the frontend and API have
always been served from different origins (different ports locally via Docker
Compose, separate hosts once deployed to Azure Container Apps), with the browser
calling the API directly and no reverse proxy in between. This went unnoticed
through the entire guest feedback flow (Milestone 6) and every dashboard PR
(Milestone 9) because every frontend test stubs `fetch` directly rather than
exercising a real browser's CORS enforcement — the first person to actually
click through the dashboard in a browser hit `No 'Access-Control-Allow-Origin'
header is present`. Fixed with `builder.Services.AddCors()`/`app.UseCors()` in
`Program.cs`, allowing exactly `FrontendOptions.BaseUrl` (the same
already-config-driven setting used to build magic-link URLs in emails — no new
setting to keep in sync) with any header/method, which covers both the plain
guest-flow requests and `authorizedFetch`'s custom `DevPersonId` header.
Registered unconditionally (not gated to Development) since the deployed
environment will need this too, once frontend and API are on separate Azure
Container Apps hosts.

### Dev seed data (local Development only)

`DevDataSeeder.SeedAsync` (called from `Program.cs`, gated to
`app.Environment.IsDevelopment()` specifically — not the broader
"not Production" gate the dev auth scheme/`GET /dev/people` use, since
integration tests run in a "Testing" environment against a fresh database
per test class and would have this seed data corrupt their fixtures)
inserts one Person per role plus one plain report, only when the `People`
table is completely empty: **Ada Admin** (Admin), **Lee Lead** (Practice
Lead, set as the seeded Practice's lead), **Morgan Manager** (Line Manager),
and **Riley Report** (no roles, reports to Morgan). All four in one seeded
Department/Practice. This exists purely so the dev sign-in picker
(`GET /dev/people`, CBLT-304) has real people to switch between locally
without first needing an existing Admin to create any — the same
bootstrapping gap that motivated `GET /dev/people` itself. Runs once per
fresh database (idempotent via the empty-table check) — safe to restart the
API repeatedly without duplicating rows.

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
