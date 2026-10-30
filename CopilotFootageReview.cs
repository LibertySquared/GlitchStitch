using System.Text.Json;

namespace ReelGood;

internal sealed record CopilotFootageReview(string Evidence, IReadOnlyList<CopilotProposal> Proposals);
internal sealed record FootageVerdict(string Verdict, string Reason, string Observations, double? EventOffset, double? StartOffset, double? EndOffset)
{
    public int FramesSupplied { get; init; }
    public int AudioTracksSupplied { get; init; }
    public string EventLabel { get; init; } = "Other";
    public string ContextType { get; init; } = "Other";
    public IReadOnlyList<EditorialRange> QuietAudioRanges { get; init; } = [];
    public string Display => $"AI suggests {Verdict}: {Reason}\nObserved: {Observations}\nScene context: {ContextType}";
    public static bool IsVerifiedOutcome(string track, string label) => track == "Video frames" &&
        !label.StartsWith("Closest video candidate:", StringComparison.OrdinalIgnoreCase) &&
        (label.Contains("eliminat", StringComparison.OrdinalIgnoreCase) || label.Contains("kill", StringComparison.OrdinalIgnoreCase));
    public FootageVerdict PreserveVerifiedOutcome(string track, string label) => Verdict == "SKIP" && IsVerifiedOutcome(track, label)
        ? this with { Verdict = "UNCERTAIN", Reason = "Glitchy did not confirm the event, but the detector reported verified outcome text. Review this disagreement rather than automatically skipping it." } : this;
    public static JsonElement Schema { get; } = JsonSerializer.Deserialize<JsonElement>("""
      {"type":"object","additionalProperties":false,"required":["verdict","reason","observations","context_type","event_type","event_offset","start_offset","end_offset"],"properties":{
        "verdict":{"type":"string","enum":["KEEP","SKIP","UNCERTAIN"]},"reason":{"type":"string","minLength":12},"observations":{"type":"string","minLength":12},
        "event_type":{"type":"string","enum":["Laughter","Elimination","Excitement","Highlight","Dialogue","Cutscene","Other"]},
        "context_type":{"type":"string","enum":["Cutscene","CharacterDialogue","PlayerSpeech","Action","Downtime","Other"]},
        "event_offset":{"type":["number","null"]},"start_offset":{"type":["number","null"]},"end_offset":{"type":["number","null"]}}}
      """);
    public static FootageVerdict Parse(string json, double length)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        var verdict = root.GetProperty("verdict").GetString()?.ToUpperInvariant();
        if (verdict is not ("KEEP" or "SKIP" or "UNCERTAIN")) throw new JsonException("The footage reviewer returned an invalid verdict.");
        var reason = root.GetProperty("reason").GetString()?.Trim(); var observed = root.GetProperty("observations").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(observed)) throw new JsonException("The reviewer did not describe its evidence.");
        double? Offset(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0 && number <= length ? number : null;
        var start = Offset("start_offset"); var end = Offset("end_offset");
        if (start is double a && end is double b && b <= a) { start = null; end = null; }
        var label = root.TryGetProperty("event_type", out var eventType) && eventType.ValueKind == JsonValueKind.String ? eventType.GetString() : "Other";
        var context = root.TryGetProperty("context_type", out var contextType) && contextType.ValueKind == JsonValueKind.String ? contextType.GetString() : "Other";
        return new(verdict, reason!, observed!, Offset("event_offset"), start, end)
        { EventLabel = label is "Laughter" or "Elimination" or "Excitement" or "Highlight" or "Dialogue" or "Cutscene" ? label : "Other",
            ContextType = context is "Cutscene" or "CharacterDialogue" or "PlayerSpeech" or "Action" or "Downtime" ? context : "Other" };
    }
}

internal sealed record FootageWindow(int? BookmarkId, double Center, string Label);
internal static class FootageReviewPlanner
{
    public static IReadOnlyList<FootageWindow> Build(IEnumerable<FootageWindow> candidates, double duration, int budget)
    {
        if (!double.IsFinite(duration) || duration <= 0) return [];
        budget = Math.Clamp(budget, 1, 24);
        var selected = new List<FootageWindow>();
        var candidateBudget = Math.Max(1, budget * 2 / 3);
        foreach (var candidate in candidates)
        {
            if (double.IsFinite(candidate.Center) && candidate.Center >= 0 && candidate.Center <= duration && selected.All(item => Math.Abs(item.Center - candidate.Center) >= 6)) selected.Add(candidate);
            if (selected.Count >= candidateBudget) break;
        }
        var scouts = budget - selected.Count;
        for (var i = 0; i < scouts; i++)
        {
            var center = duration * (i + .5) / scouts;
            if (selected.All(item => Math.Abs(item.Center - center) >= 6)) selected.Add(new(null, center, "Scout for requested moments"));
        }
        return selected;
    }
}

