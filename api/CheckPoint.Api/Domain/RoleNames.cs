namespace CheckPoint.Api.Domain;

// The exact, closed set of valid role names (spec Section 2). Used both to seed the
// Role table via migration and as constants wherever code needs to reference a
// specific role, instead of a magic string.
public static class RoleNames
{
    public const string Admin = "Admin";
    public const string PracticeLead = "Practice Lead";
    public const string LineManager = "Line Manager";
}
