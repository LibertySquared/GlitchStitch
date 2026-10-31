using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace GlitchStich;

internal sealed record YouTubeSettings(bool AutoUpload = false, string Privacy = "private", string Description = "", string Tags = "", bool MadeForKids = false, string Channel = "");
internal sealed record YouTubeDesktopClient(string Id, string Secret);

internal sealed class ProtectedYouTubeStore(string folder) : IDataStore
{
    private string FileFor<T>(string key) => Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName + ":" + key))) + ".bin");
    private string LegacyFileFor<T>(string key) => Path.Combine(folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(typeof(T).FullName!.Replace("GlitchStich.", "ReelGood.", StringComparison.Ordinal) + ":" + key))) + ".bin");
    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(folder); var clear = JsonSerializer.SerializeToUtf8Bytes(value);
        try { ProjectFileStore.Write(FileFor<T>(key), Convert.ToBase64String(ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser))); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        return Task.CompletedTask;
    }
    public Task<T> GetAsync<T>(string key)
    {
        var path = FileFor<T>(key);
        if (!File.Exists(path)) path = LegacyFileFor<T>(key);
        if (!File.Exists(path)) return Task.FromResult(default(T)!);
        var clear = ProtectedData.Unprotect(Convert.FromBase64String(File.ReadAllText(path)), null, DataProtectionScope.CurrentUser);
        try { return Task.FromResult(JsonSerializer.Deserialize<T>(clear)!); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public Task DeleteAsync<T>(string key) { foreach (var path in new[] { FileFor<T>(key), LegacyFileFor<T>(key) }.Distinct()) foreach (var file in new[] { path, path + ".bak" }) if (File.Exists(file)) File.Delete(file); return Task.CompletedTask; }
    public Task ClearAsync()
    {
        if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder, "*.bin*")) File.Delete(file);
        return Task.CompletedTask;
    }
}

internal sealed class YouTubeConnection
{
    private readonly string _folder; private readonly ProtectedYouTubeStore _store;
    private readonly Google.Apis.Http.IHttpClientFactory? _httpFactory;
    private static readonly string[] Scopes = [YouTubeService.Scope.YoutubeUpload, YouTubeService.Scope.YoutubeReadonly];
    public YouTubeSettings Settings { get; private set; } = new();
    public YouTubeConnection(string settingsDirectory, Google.Apis.Http.IHttpClientFactory? httpFactory = null)
    {
        _folder = Path.Combine(settingsDirectory, "YouTube"); _store = new(Path.Combine(_folder, "Auth")); _httpFactory = httpFactory;
        try { if (File.Exists(Path.Combine(_folder, "settings.json"))) Settings = JsonSerializer.Deserialize<YouTubeSettings>(File.ReadAllText(Path.Combine(_folder, "settings.json"))) ?? new(); } catch { }
    }
    public void SaveSettings(YouTubeSettings settings)
    {
        if (settings.Privacy is not ("private" or "unlisted" or "public")) throw new ArgumentException("Choose Private, Unlisted or Public.");
        Settings = settings; Directory.CreateDirectory(_folder); ProjectFileStore.Write(Path.Combine(_folder, "settings.json"), JsonSerializer.Serialize(settings));
    }
    public async Task<bool> HasClientAsync() => await _store.GetAsync<YouTubeDesktopClient>("client") is not null;
    private static string UserKey(YouTubeDesktopClient client) => "channel-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(client.Id)));
    public async Task<bool> IsConnectedAsync()
    {
        var client = await _store.GetAsync<YouTubeDesktopClient>("client"); return client is not null && await _store.GetAsync<TokenResponse>(UserKey(client)) is not null;
    }
    public async Task ImportClientAsync(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("installed", out var installed)) throw new InvalidDataException("Import a Google OAuth Desktop app JSON file, not a web/server client.");
        var id = installed.GetProperty("client_id").GetString(); var secret = installed.GetProperty("client_secret").GetString();
        if (string.IsNullOrWhiteSpace(id) || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(secret)) throw new InvalidDataException("The desktop OAuth client file is incomplete.");
        await _store.ClearAsync(); await _store.StoreAsync("client", new YouTubeDesktopClient(id, secret)); SaveSettings(Settings with { AutoUpload = false, Channel = "" });
    }
    private async Task<UserCredential> CredentialAsync(bool interactive, CancellationToken cancellation)
    {
        var client = await _store.GetAsync<YouTubeDesktopClient>("client") ?? throw new InvalidOperationException("Import your Google Desktop OAuth client JSON in YouTube setup first.");
        var secrets = new ClientSecrets { ClientId = client.Id, ClientSecret = client.Secret };
        if (interactive) return await GoogleWebAuthorizationBroker.AuthorizeAsync(secrets, Scopes, UserKey(client), cancellation, _store);
        var token = await _store.GetAsync<TokenResponse>(UserKey(client)) ?? throw new InvalidOperationException("Connect your YouTube account before uploading.");
        var flow = new GoogleAuthorizationCodeFlow(new() { ClientSecrets = secrets, Scopes = Scopes, DataStore = _store, HttpClientFactory = _httpFactory });
        return new UserCredential(flow, UserKey(client), token);
    }
    private YouTubeService Service(UserCredential credential) => new(new BaseClientService.Initializer { HttpClientInitializer = credential, ApplicationName = "GlitchStich", HttpClientFactory = _httpFactory });
    public async Task<string> ConnectAsync(CancellationToken cancellation)
    {
        var credential = await CredentialAsync(true, cancellation); using var flow = credential.Flow as IDisposable; using var service = Service(credential);
        var request = service.Channels.List("snippet"); request.Mine = true; var result = await request.ExecuteAsync(cancellation);
        var channel = result.Items?.FirstOrDefault()?.Snippet?.Title ?? throw new InvalidOperationException("This Google account has no YouTube channel available. Create a channel and reconnect.");
        SaveSettings(Settings with { Channel = channel }); return channel;
    }
    public async Task DisconnectAsync(CancellationToken cancellation)
    {
        var client = await _store.GetAsync<YouTubeDesktopClient>("client");
        if (client is not null)
        {
            try { var credential = await CredentialAsync(false, cancellation); using var flow = credential.Flow as IDisposable; await credential.RevokeTokenAsync(cancellation); }
            finally { await _store.DeleteAsync<TokenResponse>(UserKey(client)); SaveSettings(Settings with { AutoUpload = false, Channel = "" }); }
        }
    }
    public async Task<string> UploadAsync(string path, string title, YouTubeSettings settings, IProgress<(long Sent, long Total)>? progress, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100 || title.Contains('<') || title.Contains('>')) throw new ArgumentException("Use a video title of 1–100 characters without angle brackets.");
        if (settings.Description.Length > 5000 || settings.Tags.Length > 500 || settings.Privacy is not ("private" or "unlisted" or "public")) throw new ArgumentException("Check the description, tags and privacy setting.");
        var credential = await CredentialAsync(false, cancellation); using var flow = credential.Flow as IDisposable; using var service = Service(credential); service.HttpClient.Timeout = TimeSpan.FromMinutes(3);
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length == 0) throw new InvalidDataException("The video file is empty.");
        var video = new Video { Snippet = new VideoSnippet { Title = title.Trim(), Description = settings.Description, Tags = settings.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(), CategoryId = "20" }, Status = new VideoStatus { PrivacyStatus = settings.Privacy, SelfDeclaredMadeForKids = settings.MadeForKids } };
        var upload = service.Videos.Insert(video, "snippet,status", file, "video/mp4"); upload.NotifySubscribers = false; upload.ChunkSize = ResumableUpload.MinimumChunkSize * 4;
        upload.ProgressChanged += state => progress?.Report((state.BytesSent, file.Length));
        var result = await upload.UploadAsync(cancellation);
        if (result.Status != UploadStatus.Completed || string.IsNullOrWhiteSpace(upload.ResponseBody?.Id)) throw result.Exception ?? new InvalidOperationException("YouTube did not confirm a completed upload.");
        var url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(upload.ResponseBody.Id);
        ProjectFileStore.Write(Path.Combine(_folder, "last-upload.json"), JsonSerializer.Serialize(new { completedAt = DateTimeOffset.Now, file = path, url, settings.Privacy }));
        return url;
    }
}
