using CheckPoint.Api.Domain;
using CheckPoint.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api;

// Dev/local-only convenience: a small named cast of People/Projects/POCs/
// FeedbackRequests covering every meaningful edge case in the system (org
// visibility, cycle-engine states, flagging/catch-up, non-response tracking,
// retention, ...), so `docker compose up` alone is enough to click through the
// whole app instead of producing every state by hand via curl/Swagger first.
// Only ever runs outside Production (same gate as
// DevPersonAuthenticationHandler/DevEndpoints) and only when the People table
// is completely empty, so it never runs against a database that already has
// real or previously-seeded data.
//
// Design notes (see the seed-data expansion plan for the full rationale):
// - ProjectService.AddPersonAsync is used to attach People to Projects so the
//   New Starter schedule/stage wiring is exactly what production code
//   produces; individual requests are then backdated by hand (via
//   BackdateMembership below) to demonstrate states that haven't naturally
//   occurred yet in real time.
// - FeedbackCycleService's real hooks (HandleCheckInFlaggedAsync,
//   TriggerAdHocReviewAsync, HandleFeedbackRequestCompletedAsync) are called
//   directly for flagging/ad-hoc/general-cycle-enrolment states, so the
//   6-week insert / Under Review / General cycle semantics are exactly what
//   production code produces rather than hand-approximated. Reminder Remy and
//   Guest-Ready Gary likewise go through the real
//   RequestDispatchService.DispatchManuallyAsync/SendReminderAsync (CBLT-317)
//   rather than hand-set Status/MagicLink rows.
// - Everything else (submissions, historical magic link states, audit log
//   rows) is hand-inserted, since the real services only ever operate "as of
//   now" and several of these states need specific historical timestamps.
// - A final safety-net pass pushes any still-Scheduled request whose
//   ScheduledFor has (as a side effect of backdating) drifted into the past
//   out to a safe future date — RequestDispatchBackgroundService polls every
//   minute in Development and would otherwise try to dispatch it a second
//   time. Email sends themselves are safe to actually run in dev now (CBLT-317's
//   DevEmailSender just logs instead of hitting a real, unconfigured SMTP
//   server), but a request should still only ever be dispatched once.
public static class DevDataSeeder
{
    public static async Task SeedAsync(
        CheckPointDbContext db,
        ProjectService projectService,
        FeedbackCycleService feedbackCycleService,
        RequestDispatchService requestDispatchService,
        AuditLogService auditLogService,
        TimeProvider timeProvider)
    {
        if (await db.People.AnyAsync())
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        var adminRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.Admin);
        var practiceLeadRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.PracticeLead);
        var lineManagerRole = await db.Roles.SingleAsync(r => r.Name == RoleNames.LineManager);

        // ---- Org structure: two Departments/Practices, so cross-practice
        // scenarios (orphaning, admin-as-LM, multi-role) have somewhere to
        // happen. ----
        var techDept = new Department { Name = "Tech & Data" };
        var softwareEngineering = new Practice { Name = "Software Engineering", Department = techDept };
        var clientServicesDept = new Department { Name = "Client Services" };
        var delivery = new Practice { Name = "Delivery", Department = clientServicesDept };
        db.Departments.AddRange(techDept, clientServicesDept);
        db.Practices.AddRange(softwareEngineering, delivery);
        await db.SaveChangesAsync();

        // ---- Managers/leads first (everyone else's LineManagerId points at one
        // of these). Ada Admin also holds Line Manager (CBLT-315's "an Admin can
        // concurrently be someone's Line Manager" case); Priya Practice also
        // holds Line Manager (multi-role, additive-permissions case). ----
        var ada = new Person { FullName = "Ada Admin", Email = "ada.admin@example.com", PracticeId = softwareEngineering.Id, Roles = [adminRole, lineManagerRole] };
        var lee = new Person { FullName = "Lee Lead", Email = "lee.lead@example.com", PracticeId = softwareEngineering.Id, Roles = [practiceLeadRole] };
        var morgan = new Person { FullName = "Morgan Manager", Email = "morgan.manager@example.com", PracticeId = softwareEngineering.Id, Roles = [lineManagerRole] };
        var priya = new Person { FullName = "Priya Practice", Email = "priya.practice@example.com", PracticeId = delivery.Id, Roles = [practiceLeadRole, lineManagerRole] };
        var chris = new Person { FullName = "Cross-Practice Chris", Email = "chris.crosspractice@example.com", PracticeId = softwareEngineering.Id, Roles = [lineManagerRole] };
        db.People.AddRange(ada, lee, morgan, priya, chris);
        await db.SaveChangesAsync();

        softwareEngineering.PracticeLeadId = lee.Id;
        delivery.PracticeLeadId = priya.Id;
        await db.SaveChangesAsync();

        // ---- Everyone else. Most report to Morgan in Software Engineering;
        // a few demonstrate a specific org-structure edge case. ----
        var riley = new Person { FullName = "Riley Report", Email = "riley.report@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var max = new Person { FullName = "Multi-Project Max", Email = "max.multiproject@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var carla = new Person { FullName = "Completed-Project Carla", Email = "carla.completed@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var liam = new Person { FullName = "Leaver Recent Liam", Email = "liam.leaver@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var erin = new Person { FullName = "Leaver Expired Erin", Email = "erin.leaver@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var fiona = new Person { FullName = "Flagged Fiona", Email = "fiona.flagged@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var ethan = new Person { FullName = "Escalated Ethan", Email = "ethan.escalated@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var rosa = new Person { FullName = "Resolved Rosa", Email = "rosa.resolved@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var adrian = new Person { FullName = "Ad-hoc Adrian", Email = "adrian.adhoc@example.com", PracticeId = softwareEngineering.Id, LineManagerId = ada.Id };
        var nadia = new Person { FullName = "NoResponse Nadia", Email = "nadia.noresponse@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var remy = new Person { FullName = "Reminder Remy", Email = "remy.reminder@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var ivan = new Person { FullName = "Incomplete-POC Ivan", Email = "ivan.incomplete@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var gina = new Person { FullName = "General-Cycle Gina", Email = "gina.general@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var gary = new Person { FullName = "Guest-Ready Gary", Email = "gary.guestready@example.com", PracticeId = softwareEngineering.Id, LineManagerId = morgan.Id };
        var olivia = new Person { FullName = "Orphan Olivia", Email = "olivia.orphan@example.com", PracticeId = softwareEngineering.Id };
        var dana = new Person { FullName = "Delivery Dana", Email = "dana.delivery@example.com", PracticeId = delivery.Id, LineManagerId = chris.Id };
        var devon = new Person { FullName = "Priya's Report Devon", Email = "devon.delivery@example.com", PracticeId = delivery.Id, LineManagerId = priya.Id };
        db.People.AddRange(riley, max, carla, liam, erin, fiona, ethan, rosa, adrian, nadia, remy, ivan, gina, gary, olivia, dana, devon);
        await db.SaveChangesAsync();

        // ---- Projects. Carla gets her own so completing it doesn't cancel
        // anyone else's requests; Max gets a second so he's on two concurrent
        // Active projects; everyone else shares Atlas Platform. ----
        var atlas = (await projectService.CreateProjectAsync("Atlas Platform")).Project!;
        var beacon = (await projectService.CreateProjectAsync("Beacon Migration")).Project!;
        var harborLegacy = (await projectService.CreateProjectAsync("Harbor Legacy")).Project!;

        async Task<ProjectMembership> AddToProjectAsync(Guid projectId, Guid personId)
        {
            await projectService.AddPersonAsync(projectId, personId);
            return await db.ProjectMemberships.SingleAsync(
                m => m.ProjectId == projectId && m.PersonId == personId && m.RemovedAt == null);
        }

        async Task<Poc> AddPocAsync(Guid membershipId, string name, string email, PocRelationship relationship, PocRole role)
        {
            var poc = new Poc { ProjectMembershipId = membershipId, Name = name, Email = email, Relationship = relationship, Role = role };
            db.Pocs.Add(poc);
            await db.SaveChangesAsync();
            return poc;
        }

        // Re-anchors a membership's New Starter schedule to a backdated
        // JoinedAt, so a stage we're about to mark Sent/Submitted/NoResponse
        // reads as having genuinely happened in the past rather than "today."
        async Task BackdateMembershipAsync(ProjectMembership membership, DateTimeOffset joinedAt)
        {
            membership.JoinedAt = joinedAt;
            var requests = await db.FeedbackRequests
                .Where(r => r.ProjectMembershipId == membership.Id)
                .ToListAsync();
            foreach (var request in requests)
            {
                var weeks = request.Stage switch
                {
                    FeedbackRequestStage.NewStarterWeek2 => 2,
                    FeedbackRequestStage.NewStarterWeek4 => 4,
                    FeedbackRequestStage.NewStarterWeek8 => 8,
                    _ => (int?)null,
                };
                if (weeks is { } w)
                {
                    request.ScheduledFor = joinedAt.AddDays(w * 7);
                }
            }
            await db.SaveChangesAsync();
        }

        async Task<FeedbackRequest> StageRequestAsync(Guid membershipId, FeedbackRequestStage stage) =>
            await db.FeedbackRequests.SingleAsync(r => r.ProjectMembershipId == membershipId && r.Stage == stage);

        // Backdated, already-consumed submission — bypasses
        // FeedbackSubmissionService (which always timestamps "now") so the
        // whole history can be placed in the past. Mirrors the SentAt-set
        // LmNotification suppression so the background dispatcher doesn't try
        // to really send an email moments after the app starts.
        async Task SubmitAsync(FeedbackRequest request, Poc poc, DateTimeOffset submittedAt, string doingWell, string notDoingWell, string needsToImprove, Guid? lineManagerId)
        {
            request.Status = FeedbackRequestStatus.Sent;
            db.MagicLinks.Add(new MagicLink
            {
                Token = $"seed-{Guid.NewGuid():N}",
                FeedbackRequestId = request.Id,
                PocId = poc.Id,
                IssuedAt = submittedAt.AddDays(-1),
                ExpiresAt = submittedAt.AddDays(6),
                UsedAt = submittedAt,
            });
            var submission = new FeedbackSubmission
            {
                FeedbackRequestId = request.Id,
                PocId = poc.Id,
                DoingWell = doingWell,
                NotDoingWell = notDoingWell,
                NeedsToImprove = needsToImprove,
                SubmittedAt = submittedAt,
            };
            db.FeedbackSubmissions.Add(submission);
            await db.SaveChangesAsync();

            if (lineManagerId is { } lmId)
            {
                db.LmNotifications.Add(new LmNotification
                {
                    FeedbackSubmissionId = submission.Id,
                    LineManagerId = lmId,
                    CreatedAt = submittedAt,
                    SentAt = submittedAt,
                });
                await db.SaveChangesAsync();
            }
        }

        // A request sent to a POC who never responded — MagicLink expired
        // unanswered, no FeedbackSubmission.
        async Task MarkNoResponseAsync(FeedbackRequest request, Poc poc, DateTimeOffset issuedAt)
        {
            request.Status = FeedbackRequestStatus.Sent;
            db.MagicLinks.Add(new MagicLink
            {
                Token = $"seed-{Guid.NewGuid():N}",
                FeedbackRequestId = request.Id,
                PocId = poc.Id,
                IssuedAt = issuedAt,
                ExpiresAt = issuedAt.AddDays(7),
            });
            await db.SaveChangesAsync();
        }

        // ---- Riley Report: the on-track baseline — one project, one fully
        // submitted check-in, nothing outstanding, all three POC relationship
        // types represented on a single membership. ----
        var rileyMembership = await AddToProjectAsync(atlas.Id, riley.Id);
        await BackdateMembershipAsync(rileyMembership, now.AddDays(-21));
        var rileyTech = await AddPocAsync(rileyMembership.Id, "Tara Tech", "tara.tech@example.com", PocRelationship.Internal, PocRole.Tech);
        var rileyDm = await AddPocAsync(rileyMembership.Id, "Diego Delivery", "diego.delivery@example.com", PocRelationship.External, PocRole.Dm);
        var rileyOther = await AddPocAsync(rileyMembership.Id, "Cara Client", "cara.client@example.com", PocRelationship.Client, PocRole.Other);
        var rileyWeek2 = await StageRequestAsync(rileyMembership.Id, FeedbackRequestStage.NewStarterWeek2);
        await SubmitAsync(rileyWeek2, rileyTech, now.AddDays(-6), "Picked up the codebase fast and is already shipping.", "Sometimes goes quiet on the project channel.", "Post more progress updates mid-week.", morgan.Id);
        await SubmitAsync(rileyWeek2, rileyDm, now.AddDays(-5), "Great client-facing manner.", "Estimates have been a little optimistic.", "Pad estimates slightly for unknowns.", morgan.Id);
        await SubmitAsync(rileyWeek2, rileyOther, now.AddDays(-5), "Very collaborative with the wider team.", "Nothing significant.", "Keep doing what they're doing.", morgan.Id);

        // ---- Multi-Project Max: two concurrent Active projects, independent
        // cycles, both freshly joined. ----
        await AddToProjectAsync(atlas.Id, max.Id);
        await AddToProjectAsync(beacon.Id, max.Id);

        // ---- Completed-Project Carla: her one project is completed, which
        // cancels her outstanding requests and excludes her from further
        // scheduling — isolated to her own Project so nobody else is affected. ----
        await AddToProjectAsync(harborLegacy.Id, carla.Id);
        await projectService.CompleteProjectAsync(harborLegacy.Id);

        // ---- Leaver Recent Liam: within the 6-month retention window, still
        // visible. Marking Leaver cancels outstanding requests (mirroring
        // PersonService.MarkAsLeaverForViewerAsync's real effect). ----
        var liamMembership = await AddToProjectAsync(atlas.Id, liam.Id);
        liam.Status = PersonStatus.Leaver;
        liam.LeaverSince = now.AddMonths(-2);
        foreach (var request in await db.FeedbackRequests.Where(r => r.ProjectMembershipId == liamMembership.Id && r.Status == FeedbackRequestStatus.Scheduled).ToListAsync())
        {
            request.Status = FeedbackRequestStatus.Cancelled;
        }
        await db.SaveChangesAsync();

        // ---- Leaver Expired Erin: past the 6-month retention threshold —
        // LeaverRetentionBackgroundService (runs once immediately on startup,
        // then daily) purges her feedback for real shortly after this seed
        // completes, demonstrating retention live rather than as a fixed
        // snapshot. ----
        var erinMembership = await AddToProjectAsync(atlas.Id, erin.Id);
        await BackdateMembershipAsync(erinMembership, now.AddDays(-56));
        var erinPoc = await AddPocAsync(erinMembership.Id, "Ellery External", "ellery.external@example.com", PocRelationship.External, PocRole.Tech);
        var erinWeek2 = await StageRequestAsync(erinMembership.Id, FeedbackRequestStage.NewStarterWeek2);
        await SubmitAsync(erinWeek2, erinPoc, now.AddDays(-47), "Solid technical delivery.", "Communication could be more proactive.", "Flag blockers earlier.", morgan.Id);
        erin.Status = PersonStatus.Leaver;
        erin.LeaverSince = now.AddMonths(-7);
        foreach (var request in await db.FeedbackRequests.Where(r => r.ProjectMembershipId == erinMembership.Id && r.Status == FeedbackRequestStatus.Scheduled).ToListAsync())
        {
            request.Status = FeedbackRequestStatus.Cancelled;
        }
        await db.SaveChangesAsync();

        // ---- Flagged Fiona: Week-4 check-in flagged -> Under Review, Pending
        // CatchUp, auto-inserted Week-6 request. ----
        var fionaMembership = await AddToProjectAsync(atlas.Id, fiona.Id);
        await BackdateMembershipAsync(fionaMembership, now.AddDays(-35));
        var fionaPoc = await AddPocAsync(fionaMembership.Id, "Corin Client-Poc", "corin.clientpoc@example.com", PocRelationship.Client, PocRole.Dm);
        var fionaWeek4 = await StageRequestAsync(fionaMembership.Id, FeedbackRequestStage.NewStarterWeek4);
        await SubmitAsync(fionaWeek4, fionaPoc, now.AddDays(-8), "Technically capable.", "Missed two deadlines this sprint without flagging it in advance.", "Needs to raise risks earlier and more proactively.", morgan.Id);
        await feedbackCycleService.HandleCheckInFlaggedAsync(fionaWeek4.Id);

        // ---- Escalated Ethan: catch-up recorded as EscalateFurther ->
        // UnderReviewSince stays set despite a Recorded catch-up. ----
        var ethanMembership = await AddToProjectAsync(atlas.Id, ethan.Id);
        await BackdateMembershipAsync(ethanMembership, now.AddDays(-63));
        var ethanPoc = await AddPocAsync(ethanMembership.Id, "Tia Techlead", "tia.techlead@example.com", PocRelationship.Internal, PocRole.Tech);
        var ethanWeek8 = await StageRequestAsync(ethanMembership.Id, FeedbackRequestStage.NewStarterWeek8);
        await SubmitAsync(ethanWeek8, ethanPoc, now.AddDays(-6), "Strong individual output.", "Repeated friction with other team members on code review tone.", "Needs a direct conversation about review etiquette.", morgan.Id);
        await feedbackCycleService.HandleCheckInFlaggedAsync(ethanWeek8.Id);
        var ethanCatchUp = await db.CatchUps.SingleAsync(c => c.FeedbackRequestId == ethanWeek8.Id);
        ethanCatchUp.Status = CatchUpStatus.Recorded;
        ethanCatchUp.OutcomeType = CatchUpOutcomeType.EscalateFurther;
        ethanCatchUp.OutcomeNotes = "Concerns raised again — escalating to Practice Lead for a joint conversation.";
        ethanCatchUp.RecordedAt = now.AddDays(-2);
        await db.SaveChangesAsync(); // Person.UnderReviewSince deliberately left set — EscalateFurther doesn't clear it.

        // ---- Resolved Rosa: catch-up recorded as NoActionClosed -> cleared,
        // drops off the Flagged People view. ----
        var rosaMembership = await AddToProjectAsync(atlas.Id, rosa.Id);
        await BackdateMembershipAsync(rosaMembership, now.AddDays(-63));
        var rosaPoc = await AddPocAsync(rosaMembership.Id, "Ollie Other", "ollie.other@example.com", PocRelationship.Internal, PocRole.Other);
        var rosaWeek8 = await StageRequestAsync(rosaMembership.Id, FeedbackRequestStage.NewStarterWeek8);
        await SubmitAsync(rosaWeek8, rosaPoc, now.AddDays(-6), "Reliable and easy to work with.", "One early miscommunication about scope.", "Nothing further — since resolved.", morgan.Id);
        await feedbackCycleService.HandleCheckInFlaggedAsync(rosaWeek8.Id);
        var rosaCatchUp = await db.CatchUps.SingleAsync(c => c.FeedbackRequestId == rosaWeek8.Id);
        rosaCatchUp.Status = CatchUpStatus.Recorded;
        rosaCatchUp.OutcomeType = CatchUpOutcomeType.NoActionClosed;
        rosaCatchUp.RecordedAt = now.AddDays(-2);
        rosa.UnderReviewSince = null;
        await db.SaveChangesAsync();

        // ---- Ad-hoc Adrian: a CatchUp with no FeedbackRequestId at all — a
        // direct ad-hoc trigger, not tied to any check-in, left Pending
        // alongside Fiona in the Flagged People view. No Project/POC
        // involved at all (today's system has no validation preventing this —
        // see the ad-hoc-dispatch-validation ticket). ----
        await feedbackCycleService.TriggerAdHocReviewAsync(
            adrian.Id, callerId: ada.Id, callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        // ---- NoResponse Nadia: the same POC across all three New Starter
        // stages, none answered -> a 3-consecutive-No-Response pattern. ----
        var nadiaMembership = await AddToProjectAsync(atlas.Id, nadia.Id);
        await BackdateMembershipAsync(nadiaMembership, now.AddDays(-63));
        var nadiaPoc = await AddPocAsync(nadiaMembership.Id, "Nora Nonresponder", "nora.nonresponder@example.com", PocRelationship.External, PocRole.Tech);
        foreach (var stage in new[] { FeedbackRequestStage.NewStarterWeek2, FeedbackRequestStage.NewStarterWeek4, FeedbackRequestStage.NewStarterWeek8 })
        {
            var request = await StageRequestAsync(nadiaMembership.Id, stage);
            await MarkNoResponseAsync(request, nadiaPoc, request.ScheduledFor);
        }

        // ---- Reminder Remy: dispatched, then reminded, through the real
        // RequestDispatchService calls (CBLT-317) rather than hand-inserted
        // MagicLink rows — one superseded (invalidated) link from the
        // original dispatch, one freshly reissued and currently valid from
        // the reminder, exactly as SendReminderAsync produces in production.
        // This also doubles as CBLT-316's own regression scenario: with that
        // fix in place, the original link ends up correctly invalidated.
        var remyMembership = await AddToProjectAsync(atlas.Id, remy.Id);
        await BackdateMembershipAsync(remyMembership, now.AddDays(-21));
        var remyPoc = await AddPocAsync(remyMembership.Id, "Reggie Respondent", "reggie.respondent@example.com", PocRelationship.Internal, PocRole.Tech);
        var remyWeek2 = await StageRequestAsync(remyMembership.Id, FeedbackRequestStage.NewStarterWeek2);
        await requestDispatchService.DispatchManuallyAsync(
            remyWeek2.Id, callerId: ada.Id, callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        await requestDispatchService.SendReminderAsync(
            remyWeek2.Id, remyPoc.Id, callerId: ada.Id, callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);

        // ---- Incomplete-POC Ivan: only 1 of the 3 standard POC roles
        // assigned -> MissingStandardRoles indicator shows Dm/Other missing. ----
        var ivanMembership = await AddToProjectAsync(atlas.Id, ivan.Id);
        await AddPocAsync(ivanMembership.Id, "Isla Incomplete", "isla.incomplete@example.com", PocRelationship.Internal, PocRole.Tech);

        // ---- General-Cycle Gina: full New Starter cycle submitted and
        // completed -> auto-enrolled into the General (quarterly) cycle, next
        // FY-quarter request scheduled. ----
        var ginaMembership = await AddToProjectAsync(atlas.Id, gina.Id);
        await BackdateMembershipAsync(ginaMembership, now.AddDays(-63));
        var ginaPoc = await AddPocAsync(ginaMembership.Id, "Gus GeneralPoc", "gus.generalpoc@example.com", PocRelationship.Internal, PocRole.Tech);
        var ginaWeek2 = await StageRequestAsync(ginaMembership.Id, FeedbackRequestStage.NewStarterWeek2);
        var ginaWeek4 = await StageRequestAsync(ginaMembership.Id, FeedbackRequestStage.NewStarterWeek4);
        var ginaWeek8 = await StageRequestAsync(ginaMembership.Id, FeedbackRequestStage.NewStarterWeek8);
        await SubmitAsync(ginaWeek2, ginaPoc, ginaWeek2.ScheduledFor.AddDays(2), "Ramped up quickly.", "Nothing notable yet.", "Keep building relationships with the client team.", morgan.Id);
        await SubmitAsync(ginaWeek4, ginaPoc, ginaWeek4.ScheduledFor.AddDays(2), "Consistently good delivery.", "Nothing notable.", "Nothing further.", morgan.Id);
        await SubmitAsync(ginaWeek8, ginaPoc, ginaWeek8.ScheduledFor.AddDays(2), "Fully embedded in the team now.", "Nothing notable.", "Ready for the General cycle.", morgan.Id);
        await feedbackCycleService.HandleFeedbackRequestCompletedAsync(ginaWeek8.Id);

        // ---- Guest-Ready Gary: dispatched through the real
        // RequestDispatchService.DispatchManuallyAsync (CBLT-317) rather than
        // hand-set Status + a direct MagicLinkService.IssueAsync call, so the
        // seed data exercises the actual dispatch path end-to-end. The
        // resulting link is printed below so it can be opened in a browser
        // and submitted through the real guest flow.
        var garyMembership = await AddToProjectAsync(atlas.Id, gary.Id);
        await BackdateMembershipAsync(garyMembership, now.AddDays(-21));
        var garyPoc = await AddPocAsync(garyMembership.Id, "Gail Guest", "gail.guest@example.com", PocRelationship.Internal, PocRole.Tech);
        var garyWeek2 = await StageRequestAsync(garyMembership.Id, FeedbackRequestStage.NewStarterWeek2);
        await requestDispatchService.DispatchManuallyAsync(
            garyWeek2.Id, callerId: ada.Id, callerIsAdmin: true, callerIsPracticeLead: false, callerIsLineManager: false);
        var garyLink = await db.MagicLinks
            .Where(l => l.FeedbackRequestId == garyWeek2.Id && l.PocId == garyPoc.Id && l.InvalidatedAt == null)
            .OrderByDescending(l => l.IssuedAt)
            .FirstAsync();

        // ---- Safety net: anything still Scheduled but now due (a side effect
        // of backdating a membership above) gets pushed safely into the
        // future so the live background dispatcher never picks it up and
        // tries to really send email with no SMTP server configured. ----
        var stillDue = await db.FeedbackRequests
            .Where(r => r.Status == FeedbackRequestStatus.Scheduled && r.ScheduledFor <= now)
            .ToListAsync();
        foreach (var request in stillDue)
        {
            request.ScheduledFor = now.AddDays(21);
        }
        await db.SaveChangesAsync();

        // ---- Audit log: a couple of demo rows so the Audit Log page isn't
        // empty (no endpoint calls RecordViewAsync/RecordExportAsync yet in
        // production — see CBLT-249/CBLT-308 — so these are seeded directly). ----
        await auditLogService.RecordViewAsync(lee.Id, riley.Id);
        await auditLogService.RecordExportAsync(lee.Id, riley.Id);

        Console.WriteLine("=== DevDataSeeder: demo data seeded ===");
        Console.WriteLine("Sign in as any seeded Person via the dev sign-in picker (GET /dev/people) to explore role-scoped views.");
        Console.WriteLine("Edge cases seeded: multi-role (Ada/Priya), orphaning (Olivia/Dana), cross-practice LM (Chris), admin-as-LM tree node (Ada/Adrian),");
        Console.WriteLine("concurrent projects (Max), completed project (Carla), Leaver within/past retention (Liam/Erin), flagging + 6-week insert (Fiona),");
        Console.WriteLine("catch-up EscalateFurther/NoActionClosed (Ethan/Rosa), direct ad-hoc trigger (Adrian), non-response pattern (Nadia),");
        Console.WriteLine("manual-reminder resend (Remy), incomplete POC set (Ivan), General cycle enrolment (Gina), audit log rows (Lee viewing/exporting Riley).");
        Console.WriteLine($"Guest-Ready Gary's live, currently-valid feedback form link: /feedback/{garyLink.Token}");
        Console.WriteLine("(prefix with the frontend's base URL, e.g. http://localhost:8081/feedback/<token>, to open it in a browser)");
    }
}
