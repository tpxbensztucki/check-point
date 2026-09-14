using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheckPoint.Api.IntegrationTests;

// Mirrors Program.cs's ConfigureHttpJsonOptions (Web defaults + a string
// enum converter) — HttpClient's ReadFromJsonAsync has no way to pick up the
// server's own JsonOptions automatically, so every call in this project
// passes this explicitly to correctly parse the enum-as-string responses
// the API now sends.
public static class JsonTestOptions
{
    public static readonly JsonSerializerOptions Value = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
