using System.Text.Json;

namespace ReelGood;

internal sealed record CopilotProposal(string? Prompt, int? BookmarkId, double? Start, double? End, string? Decision, double? EventTime = null)
{
    public static JsonElement Schema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {"type":"object","additionalProperties":false,"required":["message","prompt","bookmarks"],"properties":{
          "message":{"type":"string","minLength":20},"prompt":{"type":["string","null"]},
          "bookmarks":{"type":"array","maxItems":6,"items":{"type":"object","additionalProperties":false,
            "required":["id","start","end","eventTime","leadInSeconds","decision"],"properties":{
              "id":{"type":"integer","minimum":0},"start":{"type":"number","minimum":0},"end":{"type":"number","minimum":0},
              "eventTime":{"type":["number","null"]},"leadInSeconds":{"type":["number","null"],"minimum":0,"maximum":600},"decision":{"type":["string","null"],"enum":["Keep","Skip",null]}}}}}}
        """);
    public string? Validate(double duration, int count)
    {
        if (Prompt is not null)
            return BookmarkId is not null || string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > 2000
                ? "Enter a scan prompt between 1 and 2,000 characters." : null;
        if (BookmarkId is null || BookmarkId < 0 || BookmarkId >= count) return "This bookmark is no longer available.";
        if (Start is not double start || End is not double end || !double.IsFinite(start) || !double.IsFinite(end) ||
            start < 0 || end <= start || end > duration) return "Use a start and end inside the video, with end after start.";
        if (EventTime is double moment && (!double.IsFinite(moment) || moment < start || moment > end))
            return "The corrected event time must be inside the proposed glitch.";
        return Decision is not null and not "Keep" and not "Skip" ? "Decision must be Keep, Skip, or unchanged." : null;
    }

    public static (string Message, List<CopilotProposal> Proposals) Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Expected an object with a message and proposed changes.");
        string? message = null;
        foreach (var key in new[] { "message", "explanation", "response", "reason" })
            if (root.TryGetProperty(key, out var explanation) && explanation.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(explanation.GetString()))
            { message = explanation.GetString()!.Trim(); break; }
        var proposals = new List<CopilotProposal>();
        if (root.TryGetProperty("prompt", out var prompt) && prompt.ValueKind == JsonValueKind.String)
            proposals.Add(new(prompt.GetString(), null, null, null, null));
        if (root.TryGetProperty("bookmarks", out var bookmarks) && bookmarks.ValueKind == JsonValueKind.Array)
            foreach (var item in bookmarks.EnumerateArray().Take(12))
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var index) ||
                    !item.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Number || !start.TryGetDouble(out var a) ||
                    !item.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Number || !end.TryGetDouble(out var b)) continue;
                var decision = item.TryGetProperty("decision", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var eventTime = item.TryGetProperty("eventTime", out var eventValue) && eventValue.ValueKind == JsonValueKind.Number && eventValue.TryGetDouble(out var seconds) ? (double?)seconds : null;
                // Resolve relative context in code rather than trusting model arithmetic.
                if (eventTime is double moment && double.IsFinite(moment) && item.TryGetProperty("leadInSeconds", out var lead) &&
                    lead.ValueKind == JsonValueKind.Number && lead.TryGetDouble(out var leadSeconds) && double.IsFinite(leadSeconds) && leadSeconds >= 0 && leadSeconds <= 600)
                    a = Math.Max(0, moment - leadSeconds);
                proposals.Add(new(null, index, a, b, decision, eventTime));
            }
        proposals.RemoveAll(proposal => proposal.Prompt is not null && string.IsNullOrWhiteSpace(proposal.Prompt));
        if (string.IsNullOrWhiteSpace(message) || message.Length < 12 || message.StartsWith("Review these", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Copilot is here to help", StringComparison.OrdinalIgnoreCase) || message.Contains("Glitchy is here to help", StringComparison.OrdinalIgnoreCase) || message.Contains("Let me analyze your request", StringComparison.OrdinalIgnoreCase))
        {
            if (proposals.Count == 0) throw new JsonException("The model returned no explanation or usable changes.");
            message = string.Join("\n", proposals.Select(proposal => proposal.Prompt is not null
                ? $"Proposed scan prompt: {proposal.Prompt}"
                : FormattableString.Invariant($"Bookmark #{proposal.BookmarkId}: proposed range {proposal.Start:0.###}–{proposal.End:0.###} seconds{(proposal.EventTime is double time ? $", event at {time.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} seconds" : "")}.")));
        }
        return (message, proposals);
    }
}

