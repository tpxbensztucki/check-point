using System.Net.Http.Json;

namespace CheckPoint.Api.IntegrationTests;

// Bakes JsonTestOptions.Value into every test-side JSON read, since
// HttpClient's ReadFromJsonAsync/GetFromJsonAsync have no way to pick up the
// server's own JsonSerializerOptions automatically (see JsonTestOptions.cs).
// A regex-based pass over the old ReadFromJsonAsync(...) call sites once
// missed the two GetFromJsonAsync ones when the enum-as-string fix landed;
// routing every call through these two methods means there's only one place
// left to get right.
public static class HttpJsonTestExtensions
{
    public static Task<T?> ReadJsonAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(JsonTestOptions.Value);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string requestUri) =>
        client.GetFromJsonAsync<T>(requestUri, JsonTestOptions.Value);
}
