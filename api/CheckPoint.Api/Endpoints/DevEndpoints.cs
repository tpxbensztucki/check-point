using CheckPoint.Api.Services;

namespace CheckPoint.Api.Endpoints;

// Dev-only. Deliberately unauthenticated: it exists specifically to let the
// frontend's sign-in picker (CBLT-304) show something to pick from before the
// caller has any identity at all — the same bootstrapping problem
// DevPersonAuthenticationHandler itself doesn't need to solve, since a curl
// caller already knows a Person's id from the database. Only mapped outside
// Production (see Program.cs, the same gate the dev auth scheme uses), and
// deleted together with it once CBLT-211 (real AD SSO) lands.
public static class DevEndpoints
{
    public static void MapDevEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dev");

        group.MapGet("/people", async (DevPersonDirectoryService service) => Results.Ok(await service.GetAllAsync()));
    }
}
