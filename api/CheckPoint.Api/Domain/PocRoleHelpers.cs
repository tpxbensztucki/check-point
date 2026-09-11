namespace CheckPoint.Api.Domain;

public static class PocRoleHelpers
{
    // Which of the standard set (Tech/DM/Other) have no representative among the
    // given roles — shared between PocService (a single membership's own POCs) and
    // ProjectService (POC completeness per Project when viewing a Person's list).
    public static IReadOnlyList<PocRole> ComputeMissingRoles(IEnumerable<PocRole> presentRoles)
    {
        var present = presentRoles.ToHashSet();
        return Enum.GetValues<PocRole>().Where(r => !present.Contains(r)).ToList();
    }
}
