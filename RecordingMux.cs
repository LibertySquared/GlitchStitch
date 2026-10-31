namespace GlitchStich;

internal static class RecordingMux
{
    public static List<string> Build(string video, IReadOnlyList<(string Path, string Title)> tracks, string output)
    {
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-i", video };
        foreach (var track in tracks) args.AddRange(["-i", track.Path]);
        args.AddRange(["-map", "0:v:0", "-c:v", "copy"]);
        for (var i = 0; i < tracks.Count; i++)
            args.AddRange(["-map", $"{i + 1}:a:0", $"-metadata:s:a:{i}", $"title={tracks[i].Title}"]);
        args.AddRange(["-c:a", "aac", "-af", "apad", "-shortest", "-movflags", "+faststart", output]);
        return args;
    }
}
