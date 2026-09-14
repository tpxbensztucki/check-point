namespace CheckPoint.Api.Domain;

// The single account type for every logged-in user (spec Section 2) — Admin,
// Practice Lead, and Line Manager are Roles held by a Person, not separate account
// types. A Person with no Roles has no login capability.
public class Person
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }

    // Optional today (CBLT-303) — no real sign-in exists yet to require or
    // verify one, so nothing currently depends on every Person having it. It
    // will become load-bearing once CBLT-211 (real AD SSO) and CBLT-235
    // (per-submission LM notification email) exist; until then, a null Email
    // simply means "nothing to send to yet" wherever it's needed.
    public string? Email { get; set; }

    public PersonStatus Status { get; set; } = PersonStatus.Employed;

    // Orthogonal to Status (Employed/Leaver is an employment lifecycle state;
    // being under review is a separate, overlapping flag from a flagged check-in
    // — spec Section 5.3) — set/refreshed by
    // FeedbackCycleService.HandleCheckInFlaggedAsync, never cleared here (no
    // "un-review" action exists yet).
    public DateTimeOffset? UnderReviewSince { get; set; }

    public Guid PracticeId { get; set; }
    public Practice Practice { get; set; } = null!;

    // Optional at creation — can be set later (see Edit a Person story).
    public Guid? LineManagerId { get; set; }
    public Person? LineManager { get; set; }

    // Optional escalation contact for the Person's practice, separate from their
    // direct line manager.
    public Guid? HeadOfPracticeId { get; set; }
    public Person? HeadOfPractice { get; set; }

    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
