namespace CheckPoint.Api.Services;

// The base URL guests use to reach the frontend (spec Section 9) — needed here to
// build the full magic-link URL embedded in a request email. Config-driven since
// it differs between local dev, containers, and any real deployment.
public class FrontendOptions
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; set; } = "http://localhost:8081";
}
