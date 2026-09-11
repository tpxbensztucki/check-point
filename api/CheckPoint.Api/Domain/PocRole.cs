namespace CheckPoint.Api.Domain;

// The POC's role on the project (Tech/DM/Other) — unrelated to Role/RoleNames,
// which govern a Person's own system permissions.
public enum PocRole
{
    Tech,
    Dm,
    Other,
}
