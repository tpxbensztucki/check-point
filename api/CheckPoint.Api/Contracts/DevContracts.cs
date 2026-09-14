namespace CheckPoint.Api.Contracts;

// Dev-only — see DevEndpoints.cs. Just enough to let a person-switcher screen
// search by name and know which roles it's letting someone pretend to hold.
public record DevPersonSummary(Guid Id, string FullName, IReadOnlyList<string> Roles);
