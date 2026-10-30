using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using ReelGood;

internal static class YouTubeChecks
{
    public static async Task Run()
    {
        const string folder = @"D:\Editor\ReelGood\checks\results\youtube"; Directory.CreateDirectory(folder);
        var store = new ProtectedYouTubeStore(Path.Combine(folder, "YouTube", "Auth"));
        var state = new MockState(); var connection = new YouTubeConnection(folder, new MockFactory(state));
        const string clientId = "fixture.apps.googleusercontent.com";
        await connection.ImportClientAsync("""{"installed":{"client_id":"fixture.apps.googleusercontent.com","client_secret":"fixture-secret-not-real"}}""");
        if (connection.Settings.AutoUpload || await connection.IsConnectedAsync()) throw new Exception("Importing a client enabled upload or impersonated a connected channel");
        var key = "channel-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientId)));
        await store.StoreAsync(key, new TokenResponse { AccessToken = "fixture-access-not-real", RefreshToken = "fixture-refresh-not-real", TokenType = "Bearer", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow });
        if (!await connection.IsConnectedAsync()) throw new Exception("Encrypted authorization state did not restore");
        foreach (var file in Directory.EnumerateFiles(Path.Combine(folder, "YouTube", "Auth")))
            if (File.ReadAllText(file).Contains("fixture-access") || File.ReadAllText(file).Contains("fixture-secret")) throw new Exception("An OAuth credential was stored as plaintext");
        try { await connection.ImportClientAsync("""{"web":{"client_id":"bad"}}"""); throw new Exception("A web client was accepted as a desktop client"); }
        catch (InvalidDataException) { }
        var video = Path.Combine(folder, "fixture.mp4"); await File.WriteAllBytesAsync(video, new byte[2 * 1024 * 1024 + 256]);
        var url = await connection.UploadAsync(video, "Fixture title", new(Privacy: "private", Description: "Fixture description"), null, CancellationToken.None);
        if (!url.EndsWith("fixture-video-id", StringComparison.Ordinal) || state.Bytes != new FileInfo(video).Length || state.Chunks < 2 || !state.SawPrivate || !state.SubscribersDisabled)
            throw new Exception("Mocked official SDK upload lost chunks, metadata, privacy or notification preferences");
        if (state.NonGoogleHosts != 0) throw new Exception("A YouTube request was sent to an unexpected host");
        Console.WriteLine("PASS YouTube setup: desktop-client validation, Windows-encrypted tokens, uploads disabled by default");
        Console.WriteLine("PASS Official Google SDK through mocked HTTP only: resumable chunks, private metadata, subscriber notifications disabled, completed upload receipt");
        await connection.DisconnectAsync(CancellationToken.None);
        if (await connection.IsConnectedAsync() || connection.Settings.AutoUpload) throw new Exception("Disconnect left authorization or automatic uploads active");
        Console.WriteLine("PASS YouTube disconnect clears the local token and automatic-upload preference; no real account or video was accessed");
    }

    private sealed class MockState { public long Bytes; public int Chunks, NonGoogleHosts; public bool SawPrivate, SubscribersDisabled; }
    private sealed class MockFactory(MockState state) : IHttpClientFactory
    {
        public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
        {
            var client = new ConfigurableHttpClient(new ConfigurableMessageHandler(new MockHandler(state)));
            foreach (var initializer in args.Initializers) initializer.Initialize(client);
            return client;
        }
    }
    private sealed class MockHandler(MockState state) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!; if (!uri.Host.EndsWith("googleapis.com", StringComparison.Ordinal)) state.NonGoogleHosts++;
            if (uri.AbsolutePath.EndsWith("/revoke", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, "{}");
            if (uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal)) return Json(HttpStatusCode.OK, """{"access_token":"fixture-access-not-real","token_type":"Bearer","expires_in":3600}""");
            if (request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal))
            {
                var payload = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                if (request.Content.Headers.ContentEncoding.Contains("gzip"))
                {
                    using var zipped = new System.IO.Compression.GZipStream(new MemoryStream(payload), System.IO.Compression.CompressionMode.Decompress);
                    using var clear = new MemoryStream(); await zipped.CopyToAsync(clear, cancellationToken); payload = clear.ToArray();
                }
                using var metadata = JsonDocument.Parse(payload);
                state.SawPrivate = metadata.RootElement.GetProperty("status").GetProperty("privacyStatus").GetString() == "private";
                state.SubscribersDisabled = uri.Query.Contains("notifySubscribers=false", StringComparison.OrdinalIgnoreCase);
                var response = Json(HttpStatusCode.OK, "{}"); response.Headers.Location = new Uri("https://www.googleapis.com/upload/fixture-session"); return response;
            }
            if (request.Method == HttpMethod.Put && uri.AbsolutePath.EndsWith("fixture-session", StringComparison.Ordinal))
            {
                var range = request.Content!.Headers.ContentRange!; var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                state.Chunks++; state.Bytes += bytes.Length;
                if (range.To + 1 == range.Length) return Json(HttpStatusCode.OK, """{"id":"fixture-video-id","status":{"privacyStatus":"private"}}""");
                var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.TryAddWithoutValidation("Range", "bytes=0-" + range.To); return response;
            }
            throw new Exception("Unexpected mocked YouTube request: " + request.Method + " " + uri.AbsolutePath);
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string text) => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
