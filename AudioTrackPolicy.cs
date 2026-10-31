using System.Text.Json;

namespace GlitchStich;

internal static class AudioTrackPolicy
{
    public static IEnumerable<int> Conflicts(int selected, IReadOnlyList<AudioPlaybackTrack> tracks)
    {
        var active = tracks[selected];
        for (var i = 0; i < tracks.Count; i++)
        {
            if (i == selected) continue;
            var other = tracks[i];
            if ((active.Fingerprint is not null && active.Fingerprint == other.Fingerprint) ||
                (!active.External && !other.External && active.Combined != other.Combined)) yield return i;
        }
    }
    public static string ReadLabel(JsonElement stream, int ordinal)
    {
        if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            foreach (var key in new[] { "title", "name" })
                if (tags.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    return value.GetString()!.Trim();
        return $"Audio track {ordinal}";
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static bool IsRedundantCombined(string label, IEnumerable<string> labels)
    {
        var names = labels.Select(Normalize).ToHashSet();
        return Normalize(label) == "allaudio" && names.Contains("allpcaudio") && names.Contains("microphone");
    }
}

internal sealed record AudioPlaybackTrack(bool Combined, bool External, string? Fingerprint);
