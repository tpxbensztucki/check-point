using CheckPoint.Api.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api.Services;

// Backs the dev-only sign-in picker (CBLT-304) — lets someone choose who to
// "sign in as" before they have any identity at all, which is why this one
// query is deliberately unauthenticated (see DevEndpoints.cs). Deleted
// together with DevEndpoints.cs and DevPersonAuthenticationHandler once
// CBLT-211 (real AD SSO) lands.
public class DevPersonDirectoryService(CheckPointDbContext db)
{
    public async Task<IReadOnlyList<DevPersonSummary>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await db.People
            .OrderBy(p => p.FullName)
            .Select(p => new DevPersonSummary(p.Id, p.FullName, p.Roles.Select(r => r.Name).ToList()))
            .ToListAsync(cancellationToken);
}
