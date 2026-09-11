using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CheckPoint.Api.Auth;

// Stand-in for real sign-in until CBLT-211 (AD SSO) exists. The caller identifies
// themselves by an existing Person's Id in the DevPersonId header; role claims are
// then loaded from that Person's assigned Roles in the database -- the same
// DB-driven permission model real SSO will use once it looks up the signed-in
// user's Person record. Must never be registered outside Development/Testing (see
// Program.cs) -- there is deliberately no fallback identity scheme for Production
// yet, so [Authorize]-protected endpoints simply reject everyone until CBLT-211
// lands.
public class DevPersonAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    CheckPointDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevPerson";
    public const string PersonIdHeader = "DevPersonId";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(PersonIdHeader, out var headerValue))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Guid.TryParse(headerValue, out var personId))
        {
            return AuthenticateResult.Fail($"{PersonIdHeader} is not a valid Person id.");
        }

        var person = await db.People
            .Include(p => p.Roles)
            .SingleOrDefaultAsync(p => p.Id == personId);

        if (person is null)
        {
            return AuthenticateResult.Fail("No Person found for the given id.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, person.Id.ToString()),
            new(ClaimTypes.Name, person.FullName),
        };
        claims.AddRange(person.Roles.Select(r => new Claim(ClaimTypes.Role, r.Name)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
