using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlitchStich;

internal static class BrandMigrationChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "glitchstich-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var legacy = Path.Combine(root, "ReelGoodData");
            Directory.CreateDirectory(Path.Combine(legacy, "Audio"));
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "preserve-settings");
            var oldAudio = Path.Combine(legacy, "Audio", "microphone.wav");
            File.WriteAllText(oldAudio, "independent microphone fixture");
            var migrated = AppPaths.EnsureDataDirectory(root);
            if (Directory.Exists(legacy) || File.ReadAllText(Path.Combine(migrated, "settings.json")) != "preserve-settings" || AppPaths.EnsureDataDirectory(root) != migrated) throw new Exception("Data migration lost settings or was not idempotent");
            var json = JsonSerializer.Serialize(new { VideoPath = oldAudio, Tracks = new[] { new { WavPath = oldAudio } }, MediaLibraryPaths = new[] { oldAudio }, Prompt = "Keep reactions" });
            using var restored = JsonDocument.Parse(AppPaths.RemapProjectJson(json, migrated));
            var expected = Path.Combine(migrated, "Audio", "microphone.wav");
            if (restored.RootElement.GetProperty("VideoPath").GetString() != expected || restored.RootElement.GetProperty("Tracks")[0].GetProperty("WavPath").GetString() != expected || restored.RootElement.GetProperty("MediaLibraryPaths")[0].GetString() != expected || restored.RootElement.GetProperty("Prompt").GetString() != "Keep reactions") throw new Exception("Legacy project path migration lost nested paths or prompt data");
            if (ProjectFileStore.DefaultProjectName("match.mp4") != "match_glitch.glitch" || ProjectFileStore.DefaultExportName("match.mp4") != "match_stitch.mp4") throw new Exception("Project/export naming is inconsistent");
            var auth = Path.Combine(migrated, "YouTube", "Auth"); Directory.CreateDirectory(auth);
            var legacyClientFile = Path.Combine(auth, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ReelGood.YouTubeDesktopClient:client"))) + ".bin");
            var client = new YouTubeDesktopClient("fixture.apps.googleusercontent.com", "fixture-not-real");
            var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(client), null, DataProtectionScope.CurrentUser);
            File.WriteAllText(legacyClientFile, Convert.ToBase64String(encrypted));
            var store = new ProtectedYouTubeStore(auth);
            if (store.GetAsync<YouTubeDesktopClient>("client").GetAwaiter().GetResult() != client) throw new Exception("Brand migration broke existing encrypted YouTube client storage");
            store.DeleteAsync<YouTubeDesktopClient>("client").GetAwaiter().GetResult();
            if (File.Exists(legacyClientFile)) throw new Exception("Disconnect left the old encrypted client active");
            Console.WriteLine("PASS Brand migration preserves settings, nested saved media paths and encrypted YouTube clients; glitch projects and stitch exports use distinct names");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
