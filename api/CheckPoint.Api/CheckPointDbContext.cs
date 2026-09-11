using Microsoft.EntityFrameworkCore;

namespace CheckPoint.Api;

// No domain entities yet — this exists so migrations and the local Postgres
// connection can be wired up ahead of the actual data model (Milestones 2-4).
public class CheckPointDbContext(DbContextOptions<CheckPointDbContext> options) : DbContext(options)
{
}
