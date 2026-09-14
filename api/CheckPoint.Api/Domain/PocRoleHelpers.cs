namespace CheckPoint.Api.Domain;

public static class PocRoleHelpers
{
    // Which of the standard set (Tech/DM/Other) have fewer POCs assigned than
    // the Admin-configured target count for that role (CBLT-255) — shared
    // between PocService (a single membership's own POCs) and ProjectService
    // (POC completeness per Project when viewing a Person's list). The target
    // is not a hard cap: a role with more than its target present is never
    // "missing", only one with fewer.
    public static IReadOnlyList<PocRole> ComputeMissingRoles(
        IEnumerable<PocRole> presentRoles, IReadOnlyDictionary<PocRole, int> targetCounts)
    {
        var presentCounts = presentRoles.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetValues<PocRole>()
            .Where(r => presentCounts.GetValueOrDefault(r, 0) < targetCounts.GetValueOrDefault(r, 1))
            .ToList();
    }
}
