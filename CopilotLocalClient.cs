using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReelGood;

internal static class CopilotLocalClient
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static async Task<IDisposable> AcquireAsync(CancellationToken cancellation)
    {
        await Gate.WaitAsync(cancellation); return new Lease();
    }
    private sealed class Lease : IDisposable { public void Dispose() => Gate.Release(); }
    public static async Task<string> ChatAsync(string endpoint, string model, IEnumerable<object> messages, JsonElement schema, CancellationToken cancellation)
    {
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http")
            throw new InvalidOperationException("Glitchy media stays on this PC; select a local Ollama address.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var response = await Client.PostAsJsonAsync(new Uri(uri, "api/chat"), new
        {
            model, messages, format = schema, stream = false, think = false, keep_alive = "2m",
            options = new { temperature = .1, num_ctx = 8192, num_predict = 900, num_thread = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), num_batch = 128, draft_num_predict = 0 }
        }, timeout.Token);
        var json = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Local model returned {(int)response.StatusCode}: {json[..Math.Min(400, json.Length)]}");
        using var result = JsonDocument.Parse(json);
        if (result.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
        var content = result.RootElement.GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) throw new JsonException("The model returned an empty response.");
        return content;
    }
    public static async Task UnloadAsync(string endpoint, string model)
    {
        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != "http") return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { using var response = await Client.PostAsJsonAsync(new Uri(uri, "api/generate"), new { model, prompt = "", stream = false, keep_alive = 0 }, timeout.Token); } catch { }
    }
}

