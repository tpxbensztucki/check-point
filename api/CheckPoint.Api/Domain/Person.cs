namespace CheckPoint.Api.Domain;

// The single account type for every logged-in user (spec Section 2) — Admin,
// Practice Lead, and Line Manager are Roles held by a Person, not separate account
// types. A Person with no Roles has no login capability.
public class Person
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Employed;

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
