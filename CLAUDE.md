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
  `api/CheckPoint.Api/Domain/`.
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
  accumulated real business logic inline; apply it from the start for new endpoint
  groups rather than extracting it later. Deliberately not in scope: a repository/
  interface abstraction over `CheckPointDbContext` — it wouldn't add real unit-
  testability here (rules still need the database via EF either way) and would be
  premature.
- **Extraction convention:** shared logic is pulled into a helper (e.g.
  `Services/PersonAuthorizationHelpers.cs`, `Domain/PocRoleHelpers.cs`) once it's
  about to be duplicated a *third* time, not the second — see the "Current state"
  notes below for the specific precedents. Apply the same threshold to new code.
- **Hosting:** Azure Container Apps — **on hold**: provisioning (CBLT-205/206) is
  deferred until Azure access is available. Everything else in Milestone 1 that
  doesn't need Azure is not blocked by this.
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

Prefer `docker compose up --build` from the repo root (see README) — needs only
Docker, no local SDK/Node install. See the README for running the API, frontend, or
`dotnet test` individually without it. Environment variable names are kept identical
between local Docker Compose and the (future) Azure deployment —
`ConnectionStrings__Default` for the API, `API_BASE_URL` for the frontend — so config
is copy-paste-compatible between environments.

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
  `*.test.tsx` / `*.test.ts` next to the source file. Uses Vitest + React Testing
  Library. Run with: `npm test` (in `web/`). No HTTP-mocking library (e.g. MSW) is
  set up — network calls are mocked directly with `vi.stubGlobal('fetch', vi.fn(...))`,
  `vi.unstubAllGlobals()` in an `afterEach`/`beforeEach` to reset between tests.
- Both `dotnet test` commands need a local .NET SDK, or run them via the
  `mcr.microsoft.com/dotnet/sdk:10.0` container the same way as other `dotnet`
  commands (mount the repo root, `-w /src`). The integration tests also need the
  Docker socket available (Testcontainers starts and stops the Postgres container
  itself).

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

Milestone 1 (Infrastructure & Tooling): containerisation, Docker Compose, test
project scaffolding, and PR/main CI (partial — see above) are done. Remaining:
Azure image push + deploy and Azure provisioning (CBLT-205/206), both on hold
pending Azure access.

**Auth (interim, until CBLT-211):** there is no real sign-in yet.
`api/CheckPoint.Api/Auth/DevPersonAuthenticationHandler.cs` is a stand-in: the
caller identifies themselves via a `DevPersonId` header carrying an existing
`Person`'s `Id`, and the handler loads that Person's `Role`s from the database to
build the ASP.NET Core role claims `[Authorize(Roles = ...)]` checks against.
Permissions are already DB-driven (via the Person↔Role model, Milestone 2) — when
CBLT-211 replaces this with real AD SSO, only the *identity* resolution changes
(validating an AAD token instead of a header), not the underlying role/claims model.
Registered only outside the `Production` environment (see `Program.cs`) — in
`Production`, no scheme is registered at all, so every `[Authorize]`-protected
endpoint rejects every request until real SSO exists (fails closed rather than
granting access). Local Docker Compose sets `ASPNETCORE_ENVIRONMENT=Development` on
the `api` service specifically so this stand-in works locally; never set that in a
real deployment. To call a protected endpoint locally:
```bash
curl -X POST http://localhost:8080/departments \
  -H "Content-Type: application/json" \
  -H "DevPersonId: <an-admin-persons-guid>" \
  -d '{"name":"Tech & Data"}'
```
`GET /dev/people` (`Endpoints/DevEndpoints.cs`) is deliberately unauthenticated —
it exists so the frontend sign-in picker has something to search *before* the
viewer has any identity. Registered only outside `Production`, same gate as the
dev auth scheme, and both will be deleted together once CBLT-211 lands.
`DevDataSeeder.SeedAsync` (gated to `IsDevelopment()` specifically, not the
broader "not Production" gate the other two use — integration tests run in a
"Testing" environment against a fresh database per test class and would have
this seed data corrupt their fixtures) inserts one Person per role plus one
report, only when the `People` table is empty, so the sign-in picker has real
people to switch between locally without needing an existing Admin first.
`web/src/auth/currentPerson.ts` is a small localStorage-backed accessor (**not**
a React context, deliberately — see the frontend review notes below for the
implication) holding `{ id, fullName, roles }`; `api.ts`'s `authorizedFetch`
attaches it as the `DevPersonId` header on every dashboard call, while the
guest-facing functions stay unauthenticated. `SignInPage` is a searchable
person-switcher (not a hardcoded identity) so Admin/Practice Lead/Line Manager
scoping can actually be exercised and compared in the browser.

The magic-link mechanism for guest respondents (`Services/MagicLinkService.cs`)
is deliberately independent of `FeedbackRequest` and the guest-facing form
(built later); it only ever knows an opaque `FeedbackRequestId`. Full RBAC
enforcement per the design spec's Section 8 permission matrix (CBLT-212) is
**blocked** — the spec doc isn't available (see "Linear" above) and most of the
actions it would gate don't exist as endpoints yet.

**Org & People Management:** Department/Practice/Person CRUD, role
assignment/removal, marking a Person as Leaver, cross-practice
visibility/orphan detection, and a scoped org tree view are done. Everything
under `/people` is Admin-only except `POST /people/{id}/leaver`, which a Line
Manager may also call for their own reports (a manual check against
`Person.LineManagerId`, not a plain role check — the same "manual check" pattern
recurs below for endpoints whose authorization doesn't fit a flat role). The
Leaver transition is deliberately one-way (no "un-leaver" action). Cancelling a
Leaver's outstanding feedback requests and excluding them from future cycle
enrolment is handled once the cycle engine understands `Person.Status`, not as
a separate step.

`GET /practices/{id}/people` lists People tagged to a Practice with a computed
`IsOrphaned` flag (true when a Person has no `LineManagerId`, or their Line
Manager's own `PracticeId` differs from theirs) — computed fresh on every read,
never stored, so it can't go stale when either Person's Practice or Line
Manager changes later. `Person.Email` is a nullable `string?` — deliberately
not required, since no real sign-in exists yet to require or verify one; it
becomes load-bearing once AD SSO (CBLT-211) and per-submission LM notification
email exist, until then a null `Email` just means "nothing to send to yet."

`GET /org-tree` (any authenticated caller — the role-scoping happens inside
`OrgTreeService`, not a group-level policy) returns a forest of `OrgPersonNode`
with nested `Reports`; visibility is a union of whatever the caller's roles
grant (Admin: everyone; Practice Lead: everyone in a Practice they lead; Line
Manager: themselves plus their direct reports, not deeper). A Person whose
Line Manager falls outside the caller's visible set becomes a root in the
returned forest rather than being dropped. A caller holding none of the three
roles gets an empty list, not a 403. The tree-building code guards against a
manager cycle in the data (e.g. two edits leaving A → B → A) to avoid infinite
recursion. `PUT /people/{id}` rejects a Person being set as their own Line
Manager.

The Admin Console's `DepartmentsPage`/`PeoplePage`/`PersonDetailPage`
(`/dashboard/admin/departments`, `/dashboard/admin/people[/:id]`) are the
frontend for all of the above — see "Admin Console" below for why this
milestone exists. `PersonPicker` (`web/src/components/PersonPicker.tsx`) is a
small reusable searchable Person select (mirroring `SignInPage`'s
filter-as-you-type pattern), used for Line Manager/Head of Practice selection
and reused as-is for adding a Person to a Project.

**Project & POC Management:** creating a Project, adding/removing a Person, and
completing a Project are done, Admin-only. `Person`<->`Project` is an explicit
join entity, `ProjectMembership` (not an implicit many-to-many like
`Person`/`Role`), because a Person's per-Project feedback cycle needs somewhere
to attach state to a specific Person-Project pairing. `DELETE
/projects/{id}/people/{personId}` soft-deletes (`RemovedAt`, not a row delete)
so a Person's history on a Project survives removal. Adding a Leaver to a
Project is rejected; adding a Person schedules their New Starter cycle (see
"Feedback Cycle Engine" below). `POST /projects/{id}/complete` rejects an
already-Completed Project and cancels every still-`Scheduled`
`FeedbackRequest` tied to the Project; it never touches the Person's own
status or their other Projects.

Assigning POCs is scoped to one Person's `ProjectMembership` (`Poc` entity,
cascade-deletes with its membership since it's meaningless without one).
`POST`/`GET /projects/{id}/people/{personId}/pocs` are callable by Admin, the
target Person's Practice Lead, or their Line Manager — a three-way manual
check, the same pattern used by the Leaver and Practice-view endpoints (now
extracted into `Services/PersonAuthorizationHelpers.cs` once it had been
copy-pasted a third time — see `PocService`/`RequestDispatchService`).
`MissingStandardRoles` (which of Tech/DM/Other have no active POC) is computed
fresh on every read, never stored, and is shared between `PocService` and
`ProjectService` via `Domain/PocRoleHelpers.cs`. `PUT`/`DELETE .../pocs/{pocId}`
hard-delete a single POC (no "history" requirement exists for POCs the way it
does for `ProjectMembership`). Cancelling a removed POC's outstanding feedback
request is deferred (the dispatch job that would need to look this up doesn't
exist yet); correcting a POC's email has no effect on already-sent magic links
since `MagicLink` only ever carries an opaque `FeedbackRequestId`, never the
POC's email.

`GET /people/{personId}/projects` lists every Project a Person is on, with
per-Active-Project `MissingStandardRoles` (`null` for a Completed Project).
Visibility follows the same three-way role scoping as everything else above.

The Admin Console's `ProjectsPage`/`ProjectDetailPage`
(`/dashboard/admin/projects[/:id]`) is the frontend — membership add/remove
plus, per member, full POC list/add/edit/remove.

**Guest Contact Details verification (data protection):** confirmed via direct
reads that the data model already prevents contact reuse across projects —
`Poc` is "never an existing system user, just a name/email/relationship
snapshot" scoped one-to-one to a `ProjectMembership`, with no lookup, no
matching-by-name/email, no FK to any shared "contact" entity, and no endpoint
anywhere lists `Poc` rows across memberships/Projects. The frontend's
`PocManager` form (`ProjectDetailPage.tsx`) uses plain `<input>`s with no
autocomplete/lookup wiring.

**Feedback Cycle Engine:** `FeedbackRequest` is one row per scheduled request,
carrying `ProjectMembershipId`, `ScheduledFor`, and a `Status`
(`Scheduled`/`Sent`/`Cancelled`) — it deliberately carries no snapshot of which
POCs to send to; the dispatch job resolves the Project's currently assigned
POCs at send time. `ProjectService.AddPersonAsync` schedules one
`FeedbackRequest` per configured New Starter interval (default 2/4/8 weeks)
relative to the Person's own `ProjectMembership.JoinedAt`, not the Project's
creation date, so staggered starters get staggered schedules. Interval config,
the FY-quarter skip threshold, and the automatic/manual dispatch toggle are
all now read from `AdminSettingsService` — see "Admin Settings" below for how
that switch-over happened; before it existed they were interim `IOptions<T>`
config bindings, and changing the read source didn't change any scheduling
logic itself. `ProjectService.CompleteProjectAsync` also cancels every
still-`Scheduled` `FeedbackRequest` tied to the Project. `MagicLink
.FeedbackRequestId` remains a bare, unconstrained `Guid` rather than a real FK
to `FeedbackRequest` — a deliberate decoupling from the original magic-link
design, not revisited since.

`FeedbackRequest` also carries a `Stage` (`NewStarterWeek2`/`Week4`/`Week6`/
`Week8`/`General`), separate from `Status` — needed so the cycle engine can
recognise "the 4-week check-in" reliably rather than inferring it from
`ScheduledFor` minus `JoinedAt`, which would break under a reconfigured
interval set. `FeedbackCycleService.HandleCheckInFlaggedAsync` is the
auto-insert-a-6-week-check-in hook: flagging a `NewStarterWeek4` request
schedules one `NewStarterWeek6` request (idempotent), flagging any other stage
does nothing. It sat **dangling with zero non-test callers** from the moment
it was built until the ad-hoc-review milestone wired it up (see below) — a
deliberate "ship the hook first" precedent that recurs a few times in this
project (see Audit Log/Anonymisation in "Data Protection & Export" below);
tested by calling the service directly in the meantime.

`ProjectMembership.GeneralCycleEnrolledAt` (nullable, set once, never cleared)
marks a membership as having transitioned from the New Starter cycle into the
General (quarterly) cycle — both the idempotency guard and the anchor date the
quarterly schedule counts from. Set when the `NewStarterWeek8` request (always
the last New Starter stage chronologically) concludes, unless the Project has
since Completed or the Person has since become a Leaver, in which case
enrolment is skipped. `HandleFeedbackRequestCompletedAsync` dispatches by
`Stage`: a `NewStarterWeek8` completion enrols into the General cycle *and*
schedules its first `General` request; a `General` completion schedules the
next one, continuing every FY quarter (Apr–Jun/Jul–Sep/Oct–Dec/Jan–Mar) until
the Project completes or the Person becomes a Leaver. On first enrolment, if
the next quarter boundary falls within the configured skip threshold of the
enrolment moment, that quarter is skipped in favour of the one after.
Subsequent quarters are anchored to the *previous* request's own
`ScheduledFor` (always a quarter-start date) rather than "now" processed-at
time, so the cadence never drifts. A single `General` stage value covers every
quarter — quarters have no distinct identity beyond "the next one."
Idempotency for both New Starter and General scheduling is a
does-a-later-request-already-exist check.

`HandleCheckInFlaggedAsync`, once wired up (see "Ad-hoc Review" below), always
sets `Person.UnderReviewSince` (orthogonal to `Status`: Employed/Leaver is an
employment lifecycle, being under review is a separate, overlapping flag) and
creates a pending `CatchUp` record, with the New-Starter-4-week-specific
6-week insert layered on top only for that stage. Idempotent per check-in via
a does-a-`CatchUp`-already-exist-for-this-`FeedbackRequestId` check (a
*different* check-in for the same Person still gets its own `CatchUp`).
`CatchUp` doesn't store who the LM/Practice Lead actually are — like
`Person.IsOrphaned`, that's resolved live via `Person.LineManagerId`/
`Practice.PracticeLeadId` at read time.

**Guest Feedback Form:** the guest landing page was the first real frontend UI
screen in this project (everything before it was backend-only). `GET
/magic-links/{token}` wraps `MagicLinkService.ValidateAsync` and is
deliberately unauthenticated — no `RequireAuthorization` at all, since a guest
never signs in — mapping `Valid`→200, `NotFound`→404, `Expired`→410,
`AlreadyUsed`→409. `react-router-dom` is the frontend's routing dependency;
`web/src/App.tsx` is the route table, `web/src/pages/` holds one component per
screen, `web/src/api.ts` is the fetch-wrapper convention (plain `fetch` plus
`getApiBaseUrl()`, no HTTP client library). `GuestFeedbackPage` renders one of:
loading, expired, already-submitted, invalid-link, or (once valid) the form.

`web/src/components/FeedbackForm.tsx` is the three-field form ("doing well" /
"not doing well" / "needs to improve"), all required, each capped at 2000
characters. Deliberately does **not** set an HTML `maxLength` on the
`<textarea>`s: a guest can type past the limit, see the counter turn red, and
get a blocking validation message on submit, rather than being silently
stopped mid-keystroke — matches the accessibility-conscious pattern used
throughout the guest/dashboard forms (see also `CatchUpOutcomePage` below):
the Submit button is never `disabled`; invalid attempts show inline
`role="alert"` errors and `aria-invalid`/`aria-describedby` instead, since a
silently-disabled button gives screen-reader/keyboard users no explanation for
why nothing happens.

`POST /magic-links/{token}/submission` (`FeedbackSubmissionService`) handles a
completed submission. Content validation (required + 2000-char limit,
mirroring the frontend's own rules since the API is public and can't trust
client-side validation alone) happens *before* the magic link is touched, so a
rejected submission never burns the guest's one chance to submit.
`MagicLinkService.LoadAndCheckAsync` is `internal`, not `private`, precisely
so this service can validate a token and then mutate the same tracked
`MagicLink` as part of one `SaveChangesAsync`, rather than consuming the link
before knowing the submission will succeed. One `SaveChangesAsync` marks the
`MagicLink` used, inserts the immutable `FeedbackSubmission` row, and — if the
Person has a `LineManagerId` — inserts an `LmNotification` outbox row.
`FeedbackRequest.Status` is deliberately left untouched by submission: it
tracks the request's own dispatch lifecycle, not response state, since a
request can have several currently-assigned POCs each responding
independently. There is deliberately no endpoint that edits a
`FeedbackSubmission` — immutability is enforced by omission, not a guarded
field. `LmNotification` is a durable outbox, not an actual send: it's written
in the same transaction as the submission, so it satisfies "must not be
silently dropped" by construction even before the dispatch job exists. No
line manager assigned is "nobody to notify," not a dropped notification.

`MagicLink` and `FeedbackSubmission` both carry a `PocId` (real FK to `Poc`),
and `FeedbackSubmissions`' unique index is `(FeedbackRequestId, PocId)` rather
than `FeedbackRequestId` alone — needed once per-POC email dispatch required
issuing one magic link per currently-assigned POC (not one per request), and
since outcomes are tracked per POC, not aggregated onto the request as a
whole (a request with 3 POCs can have a mix of Submitted/No-Response
outcomes). Whether a given POC has responded is always answered by whether a
`FeedbackSubmission` row exists for that `(FeedbackRequestId, PocId)` pair —
never stored as a status on `FeedbackRequest` itself.

**Notifications & Response Tracking:** `RequestDispatchService` sends the POC
feedback request email containing a magic link — one `MagicLink` (and email)
per currently-assigned POC on the request's `ProjectMembership`, each scoped
to that POC and request only. Entry points: `DispatchDueAutomaticRequestsAsync`
(polled every minute by `RequestDispatchBackgroundService`, an
`IHostedService`), `DispatchManuallyAsync` (`POST
/feedback-requests/{id}/dispatch`, same three-way scoping as `PocService`),
and `SendReminderAsync` (below). Automatic dispatch is gated by the
Admin-configured toggle (see "Admin Settings"); the manual endpoint works
regardless, since an authorised user can always force a send.
`RequestDispatchBackgroundService` is **not registered in the "Testing"
environment** (see `Program.cs`): it runs on real wall-clock time via
`Task.Delay`, which would otherwise fire unpredictably against tests that
advance a `FakeTimeProvider` instead of real time — the same
not-registered-in-Testing gate applies to `LmNotificationDispatchBackgroundService`
and `LeaverRetentionBackgroundService` below, for the same reason.

Actual email sending is behind an `IEmailSender` interface — `SmtpEmailSender`
is the real implementation (BCL `SmtpClient`; no SMTP server exists in any
environment this project has run in yet, so a misconfigured deployment fails
loudly rather than pretending to send). Tests substitute a
`RecordingEmailSender` fake. The frontend URL embedded in each email comes
from `FrontendOptions.BaseUrl` (plain infra config, not an Admin Setting),
wired via `Frontend__BaseUrl` in `docker-compose.yml`.

A due request whose `ProjectMembership.RemovedAt` is set (person left the
project after the request was scheduled) is marked `Cancelled` instead of
sent. A due request with zero currently-assigned POCs is left `Scheduled` and
retried next pass rather than marked `Sent` with nothing actually sent.

`SendReminderAsync` (`POST /feedback-requests/{id}/pocs/{pocId}/remind`)
resends to one non-responding POC — a plain resend, not a new
`FeedbackRequest`: issues a fresh `MagicLink` (fresh 7-day expiry) and
invalidates whatever prior, still-usable link(s) existed for that exact
`(FeedbackRequest, Poc)` pair. `MagicLink.InvalidatedAt` is distinct from
`UsedAt` — a superseded link was never used to submit, just replaced.
`MagicLinkService`/`FeedbackSubmissionService` both have a `Superseded` status
(mapped to `410 Gone`, same as `Expired`) so a guest with an old link sees
"replaced by a more recent one" rather than the misleading "already
submitted." Rejected with `NotYetDispatched` if the request isn't `Sent` yet,
or `AlreadySubmitted` if that POC already has a `FeedbackSubmission`.

`GetPocStatusesAsync` (`GET /feedback-requests/{id}/pocs`) reports each
currently-assigned POC's outcome (`NotYetSent`/`Sent`/`Submitted`/
`NoResponse`/`Cancelled`) — never a single status on the request as a whole,
since different POCs on the same request can be in different states.
Deliberately **computed live** on every call (given the current time, whether
a `FeedbackSubmission` exists, and the most recent non-invalidated
`MagicLink`) rather than a stored flag flipped by a background job — no risk
of drifting out of sync with "now," no extra job/infra needed. A POC added
after dispatch reads as `NotYetSent`. `RequestDispatchService.ComputePocStatus`
is `internal` (not `private`) so `DashboardService` reuses the exact same
logic batched across every request a caller can see (see "Dashboard" below).

`LmNotificationDispatchService.DispatchPendingNotificationsAsync` (polled
every minute) delivers the `LmNotification` outbox rows, never batched —
three respondents submitting at different times means three separate emails
to the LM. A pending row whose `LineManager.Email` is null is skipped (left
pending, retried) rather than treated as a failure. The email contains a
link, never the feedback content itself, pointing at
`{FrontendOptions.BaseUrl}/people/{personId}` — no magic-link-style token
needed, since an LM is a standing system user protected by ordinary
`[Authorize]`.

`PocResponseHistoryService.GetPocHistoryAsync` (`GET
/pocs/{pocId}/response-history`) tracks non-response as a pattern across
check-ins, not just the most recent one — generalizes the live per-(request,
POC) status computation across every `FeedbackRequest` sharing that POC's
`ProjectMembershipId`, most-recent-first; a request the POC was never
dispatched to is excluded from their history entirely. Returns both
`ConsecutiveNoResponseCount` and `TotalNoResponseCount` — no fixed "pattern"
threshold is invented, since none is defined; raw counts only.
`GetProjectPocPatternsAsync` (`GET /projects/{projectId}/poc-response-patterns`)
is a filtered list, not a pass/fail gate, following the same
`visiblePersonIds` union scoping as the org tree.

`PersonAuthorizationHelpers.IsAuthorizedForPersonAsync` (Admin, or the target
Person's own Line Manager, or their Practice's Lead) was extracted out of
`PocService` and `RequestDispatchService` once they'd been carrying
byte-for-byte identical copies of this check — the same "extract once
genuinely reused a third time" precedent as `PocRoleHelpers`.

**Ad-hoc Review / Flagging:** `FeedbackCycleService.FlagCheckInAsync` (`POST
/feedback-requests/{id}/flag`) was the first ticket to actually wire up
`HandleCheckInFlaggedAsync`, which had sat dangling with zero non-test callers
since it was built. It's a thin caller-aware wrapper: loads the request,
authorizes, calls the existing hook unchanged. Deliberately a **two-way**
check (Admin OR the Person's own Line Manager) rather than the three-way
`PersonAuthorizationHelpers` check used elsewhere — its own AC only ever
mentions a Line Manager, unlike ad-hoc triggering (next), whose AC explicitly
includes Practice Lead too — a deliberate, textually-supported contrast
between the two, not an oversight.

`TriggerAdHocReviewAsync` (`POST /people/{personId}/ad-hoc-review`) lets a
Practice Lead or Line Manager start a review independent of a check-in — same
underlying mechanism as flagging (set `UnderReviewSince`, add a `CatchUp`),
but with `FeedbackRequestId` left `null` and never triggering a 6-week
insert. Three-way auth here, unlike the two-way flagging check, since this
ticket's AC explicitly names both roles. `CatchUp.FeedbackRequestId` is
nullable specifically for this path — the field originally only anticipated
check-in-triggered catch-ups. This method looks for **any** `Pending`
`CatchUp` for the Person (from either path) and surfaces it instead of
creating a duplicate — a rule local to ad-hoc triggering, not a change to
flagging's own idempotency (which stays per-`FeedbackRequestId`, so flagging
two *different* check-ins for the same Person still creates two separate
`CatchUp` rows). An ad-hoc `CatchUp` already pending never suppresses a later
4-week check-in's 6-week insert — the two guards are independent.

`CatchUpService.RecordOutcomeAsync` (`POST /catch-ups/{catchUpId}/outcome`)
introduced its own service, split off from `FeedbackCycleService` once
"managing an existing CatchUp" concerns were distinct enough to warrant it —
same reasoning as `RequestDispatchService`/`PocResponseHistoryService`
splitting earlier. `CatchUpOutcomeType` (`SixWeekCheckInAdded`,
`NoActionClosed`, `EscalateFurther`, `Other`) plus `OutcomeNotes`/`RecordedAt`
— "or free-text equivalent" is satisfied by pairing `Other` with required
notes, rather than unlimited freeform categories. Three-way auth (matching
ad-hoc triggering, not flagging). Recording an outcome clears
`Person.UnderReviewSince` **unless** the outcome is `EscalateFurther` — the
review isn't over yet in that case. Rejects with `AlreadyRecorded` if the
`CatchUp` isn't still `Pending`, which — combined with the "any Pending
catch-up" check above — means a Person whose catch-up was just resolved can
immediately have a fresh one opened by a new flag or ad-hoc trigger.

`CatchUpService.GetHistoryAsync` (`GET /people/{personId}/catch-ups`) is a
Person's full flag/ad-hoc-review/catch-up-outcome history, most-recent-first,
same three-way scoping and "empty list is a normal Success" precedent as POC
response history. `UnderReviewSince` is surfaced at the top level (not just
inferred from entries) so an active review is trivially distinguishable from
resolved history. `CatchUpResponse` has a computed `TriggerSource`
(`CheckIn`/`AdHoc`, derived from whether `FeedbackRequestId` is set).

**Dashboard:** the dashboard shell and a dev-only sign-in (see "Auth" above)
had to exist before any dashboard section could land — the frontend had no
concept of "who is signed in" before this (everything earlier was either
backend-only or the unauthenticated guest flow). `DashboardLayout` +
`RequireCurrentPerson` are the shell and route guard every dashboard screen
mounts inside; `/` redirects to `/dashboard`. `OrgTreePage`
(`/dashboard/org-tree`) is pure frontend wiring over the already-role-scoped
`GET /org-tree` — no duplicate tree implementation.

`OutstandingRequestsPage`/`GET /dashboard/outstanding-requests`
(`DashboardService`) needed a genuinely new aggregate query — nothing before
it listed outstanding requests across more than one Person/POC/Project at a
time. `PersonAuthorizationHelpers.GetVisiblePersonIdsAsync` (the
union-of-visible-Person-ids scoping: Admin no filter, Practice Lead own
practice, Line Manager self + direct reports) was extracted here once it
became a *third* occurrence (after the org tree and POC response patterns) —
both existing call sites were mechanically refactored onto it in the same PR
with no behaviour change. "Outstanding" means the live-computed POC status
isn't `Submitted`. Grouping "by cycle" is a frontend concern — every entry
already carries `Stage`, so the page groups client-side rather than adding a
per-stage backend endpoint. The manual reminder action reuses the existing
remind endpoint.

`FlaggedPeoplePage`/`GET /dashboard/flagged-people`
(`DashboardService.GetFlaggedPeopleAsync`) is deliberately keyed off "has a
`Pending` `CatchUp`", not `Person.UnderReviewSince != null` — the two can
diverge: after an `EscalateFurther` outcome, `UnderReviewSince` stays set but
that `CatchUp`'s own `Status` becomes `Recorded`, so the Person correctly
stops appearing here until a fresh flag or ad-hoc trigger opens a new
`CatchUp`. Uses the same `GetVisiblePersonIdsAsync` scoping.
`CatchUpOutcomePage` (`/dashboard/people/{personId}/catch-up`) is the
navigation target flagging a Person leads to — it shows the Person's full
catch-up history with a form (only for the currently `Pending` entry)
mirroring the backend's own validation (`Other` requires notes, shown as an
inline `role="alert"` rather than a disabled submit button, matching
`FeedbackForm`'s accessibility precedent above).

`POST /people/{id}/roles` and `DELETE /people/{id}/roles/{roleName}`
assign/remove one of the fixed Role names on a Person. Assigning `Practice
Lead` requires a `PracticeId` and sets that `Practice`'s `PracticeLeadId`;
removing the role clears `PracticeLeadId` on every Practice the Person leads.
Re-assigning a role a Person already holds is rejected — left for a future
story.

**Admin Console:** added once the dashboard was actually clicked through in a
browser — there was no frontend anywhere for creating or editing a
Department, Practice, Person, Project, or POC (all backend-API-only since
Milestones 3/4). This is permanent, essential functionality, not throwaway
test tooling: AD SSO will authenticate people, but it will never supply
org/people/project data, so this application always has to be where that
data is entered and maintained. `DashboardLayout`'s nav gained an "Admin"
section, shown only when the signed-in person holds the `Admin` role (a
client-side UX nicety — real enforcement stays server-side). The specific
screens (`DepartmentsPage`, `PeoplePage`/`PersonDetailPage`,
`ProjectsPage`/`ProjectDetailPage`) are described alongside their backing
data above. All the write actions the Admin Console screens use already
existed on the backend — building these pages was new reads plus frontend
wiring, not new backend behaviour, except where noted.

**Admin Settings:** `AppSettings` is a single, lazily-created singleton row
(one per database, created with defaults the first time
`AdminSettingsService.GetAsync`/`UpdateAsync` runs against an empty table,
rather than a migration-time seed) covering every value that used to live in
a hardcoded default or an interim `IOptions<T>` placeholder
(`NewStarterCycleOptions`, `GeneralCycleOptions`, `RequestDispatchOptions`).
`GET`/`PUT /admin/settings` (`SettingsPage` at `/dashboard/admin/settings`) is
Admin-only, with baseline validation (no empty/negative values) plus a
strictly-increasing-intervals rule for the New Starter schedule (mirrored
client-side for immediate feedback, server is the authoritative guard).
`SettingsPage` groups fields into three sections (Cycle Scheduling,
Notifications, POC Requirements) purely for display — the API has one flat
row to read/write, matching every other full-field-set `PUT` convention here.

Each consuming service (`ProjectService` for the New Starter interval,
`FeedbackCycleService` for the FY-quarter skip threshold, `RequestDispatchService`
for the automatic/manual toggle, and `PocRoleHelpers`/`AdminSettingsService
.ToPocRoleTargets` for POC role-count targets) was switched over to read from
`AdminSettingsService` one at a time, each deleting its now-unused
`IOptions<T>` placeholder and `Program.cs` registration. "Already-scheduled
requests are unaffected by a later setting change" holds structurally
throughout: each setting is read once, at the moment it's needed (a Person
joining, an enrolment happening, a dispatch pass running), and baked onto the
resulting rows — there's no live re-read that could retroactively drift. The
POC role-count switch-over changed the *meaning* of "missing" from "zero
representatives of this role" to "fewer than the Admin-configured target
count," which had no prior `IOptions<T>` to delete (the old "exactly one of
each" default was an implicit assumption baked into a single-argument method
signature, not a named config class).

Every integration test that constructed one of these services directly with
an `Options.Create(...)` now passes `new AdminSettingsService(context)`
instead — a mechanical swap across roughly 20 call sites (see "Known test
brittleness" below for why this kind of change is expensive here and the
planned fix).

**Data Protection & Audit:** `AuditLogEntry` (`GET /audit-log`, `AuditLogPage`
at `/dashboard/admin/audit-log`, Admin-only) is the immutable "who
viewed/exported whose feedback, when" trail. It deliberately carries no
reference to any specific `FeedbackSubmission` — only `ViewerId`/`PersonId`/
`Action`/`OccurredAt` — so an entry survives intact once the retention job
(below) removes the feedback content it once referred to. `AuditLogService`
only ever inserts (`RecordViewAsync`/`RecordExportAsync`); there's no
update/delete, the same immutability-by-omission precedent as
`FeedbackSubmission`. `GetLogAsync` combines its four optional filters
(Person, viewer, from, to) with AND. **`RecordViewAsync`/`RecordExportAsync`
currently have no caller anywhere in the codebase** — no endpoint today
exposes a Person's actual feedback content to an internal viewer (only
aggregate status), and the only planned mechanism for seeing content is the
not-yet-built PDF export (CBLT-247), which is expected to be their first
caller. This mirrors the earlier "ship the hook first" pattern from the cycle
engine — intentional, but worth re-confirming CBLT-247 is still tracked so
this doesn't rot as unreferenced dead code.

`Person.LeaverSince` (nullable `DateTimeOffset`, set once when
`PersonService.MarkAsLeaverForViewerAsync` flips `Status` to `Leaver`, never
cleared) is the strict anchor for a 6-month retention clock.
`LeaverRetentionService.PurgeExpiredLeaversAsync` finds every Leaver whose
`LeaverSince` is 6 months or older and, per Person, hard-deletes exactly
three kinds of row scoped to their `ProjectMembership`s: `MagicLink`s
(deleted first — `PocId` is a Restrict FK to `Poc`, so these must go before
the `Poc` rows they reference), `FeedbackSubmission`s (cascades to any
`LmNotification` outbox row), and `Poc`s. The Person row, their
`ProjectMembership`s, and `FeedbackRequest` scheduling metadata are
deliberately untouched — none of it is feedback-related personal data.
`LeaverRetentionBackgroundService` follows the same `BackgroundService` +
`IServiceScopeFactory` shape as the other background jobs (not registered in
"Testing"), but polls daily rather than every minute, appropriate to a
6-month-resolution job; it also runs once immediately on startup, before its
first delay. Tested against the service directly with a `FakeTimeProvider`.

**Export & Anonymisation:** `Domain/FeedbackAnonymiser.cs` is the core content
transform — pure logic, no EF Core, no HTTP, tested entirely in
`CheckPoint.Api.UnitTests`. `IdentifiedFeedbackEntry` (the input) is
deliberately not the `FeedbackSubmission` entity itself, so the transform has
zero database dependency and stays reusable by any future anonymised view —
`Anonymise` is a plain static method over `IEnumerable<IdentifiedFeedbackEntry>`.
`AnonymisedFeedbackEntry` (the output) has no name/email/role property at
all, so there's nothing for a careless caller to accidentally forward — a
stronger guarantee than filtering fields at the call site. Ordering is
derived purely from the content itself (never submission time or POC list
position), satisfying both "no correlation clue back to a respondent" and
"deterministic given the same input." **Not yet wired to any endpoint** —
same status as the audit-log hooks above, waiting on CBLT-247 (PDF export) as
its first caller.

## Known test brittleness — planned fix

The integration test suite (`api/CheckPoint.Api.IntegrationTests`) has no
shared fixture: every test file independently boots its own
`PostgreSqlContainer` and defines its own `CreateContext()`/`CreateClient
(personId)` helper. This is why settings/serialization changes (like the
`AdminSettingsService` switch-over above, or the enum-as-string fix below)
require mechanically touching 20-70 call sites rather than one shared place —
and why that kind of change has twice caused a follow-up fix commit for a
missed call site. The frontend test suite has the same shape: no shared
`test-utils`, every `*.test.tsx` hand-rolls its own router/auth-stub
wrapper. Planned fix: a shared `IntegrationTestBase` (container lifecycle,
`CreateContext`/`CreateClient`, a JSON-aware HTTP client wrapper) on the
backend, and a `web/src/testUtils.tsx` render helper on the frontend —
migrate the highest-churn files first, the rest opportunistically.

## Notable past bugs

- **Enums serialized as raw integers, not strings** (found smoke-testing the
  Admin Console's POC form): the API never configured a
  `JsonStringEnumConverter`, so every enum silently serialized as its
  underlying integer even though every frontend TypeScript comparison assumed
  the member's name as a string. Nothing caught it because frontend unit
  tests stub `fetch` with hand-written JSON that already used the intended
  string values — only a real browser round-trip exposed it. Fixed with one
  line in `Program.cs` (`ConfigureHttpJsonOptions` + `JsonStringEnumConverter`,
  `allowIntegerValues: true` so old numeric request bodies still work).
  `api/CheckPoint.Api.IntegrationTests/JsonTestOptions.cs` mirrors this on the
  test side, since `HttpClient.ReadFromJsonAsync<T>()` has no way to pick up
  the server's own JSON options automatically.
- **No CORS configuration** (found testing dev seed data end-to-end in a
  browser): the frontend and API have always been served from different
  origins, but every frontend test stubs `fetch` directly rather than
  exercising real browser CORS enforcement, so this went unnoticed through
  the whole guest flow and dashboard. Fixed with `AddCors()`/`UseCors()` in
  `Program.cs`, allowing exactly `FrontendOptions.BaseUrl` with any
  header/method (covers both plain guest requests and `authorizedFetch`'s
  custom `DevPersonId` header). Registered unconditionally, not gated to
  Development, since the deployed environment will need it too once frontend
  and API are on separate hosts.

Both bugs share a root cause worth remembering for future work: frontend unit
tests stub `fetch` and never touch a real API, so contract mismatches between
frontend expectations and actual API behaviour (serialization shape, CORS,
headers) only surface via a manual browser click-through, not automated
tests.
