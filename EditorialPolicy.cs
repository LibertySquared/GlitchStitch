namespace ReelGood;

internal enum EditorialMode { Story, Highlights, Comedy }
internal sealed record EditorialRange(double Start, double End, string Reason);
internal sealed record DraftReviewContext(EditorialMode Mode, IReadOnlyList<EditorialRange> ProtectedContext, IReadOnlyList<EditorialRange> QuietDowntime)
{
    public EditorialMode? SecondaryMode { get; init; }
}

internal static class EditorialPolicy
{
    public static string PreferenceScope(string? game, string? gameMode, bool irl, EditorialMode intent, EditorialMode? secondary = null)
    {
        string Normalize(string? value) => Uri.EscapeDataString(string.Join(" ", (value ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        var modes = string.Join("+", Modes(intent, secondary).OrderBy(mode => (int)mode).Select(mode => (int)mode));
        return $"{(irl ? "irl" : "game")}|{(irl ? "" : Normalize(game))}|{(irl ? "" : Normalize(gameMode))}|{modes}";
    }

    public static IEnumerable<EditorialMode> Modes(EditorialMode primary, EditorialMode? secondary = null)
    {
        yield return primary;
        if (secondary is { } other && Enum.IsDefined(other) && other != primary) yield return other;
    }
    public static bool HasStory(EditorialMode primary, EditorialMode? secondary = null) => Modes(primary, secondary).Contains(EditorialMode.Story);

    public static string Name(EditorialMode mode) => mode switch
    {
        EditorialMode.Highlights => "Competitive highlights",
        EditorialMode.Comedy => "Comedy / player reactions",
        _ => "Story / preserve context"
    };

    public static string ScanQuery(string custom, EditorialMode mode, EditorialMode? secondary = null)
    {
        var targets = Modes(mode, secondary).SelectMany(selected => Targets(selected).Split(", "));
        var guidance = (custom ?? "").Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(", ", guidance.Concat(targets).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string Targets(EditorialMode mode) => mode switch
        {
            EditorialMode.Highlights => "eliminations, victories, clutch gameplay highlights, player reactions",
            EditorialMode.Comedy => "laughter, funny player reactions, group jokes",
            _ => "story highlights, character dialogue, cutscenes"
        };

    public static string Instructions(EditorialMode mode, EditorialMode? secondary = null) =>
        $"Editing intent: {string.Join(" + ", Modes(mode, secondary).Select(Name))}. " +
        string.Join(" ", Modes(mode, secondary).Select(IntentInstruction)) +
        (HasStory(mode, secondary) ? "Story preservation takes priority when modes are combined; keep unreviewed narrative scenes and retain comedic/highlight moments within that continuity. " : "Either selected mode can qualify a moment; it does not need to match both. ") +
        "In ALL modes protect character dialogue and cutscenes that supply scene context. Distinguish character dialogue from player microphone/party speech using the actual audio and visual evidence; if unsure use Other. " +
        "Classify context_type as Cutscene, CharacterDialogue, PlayerSpeech, Action, Downtime or Other. Downtime requires clear inactive waiting/loading/menu footage with no meaningful dialogue, action, joke setup or story purpose. Silence alone and quiet exploration are not proof of downtime. Be conservative; use UNCERTAIN and Other when evidence is insufficient.";

    private static string IntentInstruction(EditorialMode mode) => mode == EditorialMode.Story
        ? "Preserve story continuity, establishing scenes, character dialogue and cutscenes, including quiet cinematic pauses. "
        : mode == EditorialMode.Comedy
        ? "Prefer player jokes, laughter, reactions and their setup; ordinary character speech is context rather than proof of a joke. "
        : "Prefer verified eliminations, victories, clutch action and player reactions; routine walking, looting and waiting are not highlights by themselves. ";

    public static bool Valid(EditorialRange range, double duration) => double.IsFinite(range.Start) && double.IsFinite(range.End) &&
        range.Start >= 0 && range.End > range.Start && range.End <= duration;

    public static DraftEditPlan Apply(DraftEditPlan highlightPlan, IReadOnlyList<DraftBookmark> bookmarks, double duration,
        DraftReviewContext context, bool trimQuiet)
    {
        var protectedRanges = context.ProtectedContext.Where(range => Valid(range, duration)).ToList();
        protectedRanges.AddRange(bookmarks.Where(bookmark => bookmark.Decision == "Keep" || bookmark.IsVerifiedOutcome && bookmark.Decision != "Skip")
            .Select(bookmark => new EditorialRange(Math.Max(0, bookmark.Start), Math.Min(duration, bookmark.End), "Explicit Keep"))
            .Where(range => Valid(range, duration)));
        var story = HasStory(context.Mode, context.SecondaryMode);
        var clips = story
            ? new List<DraftClip> { new(0, duration, [], "Story continuity · unreviewed footage retained") }
            : highlightPlan.Clips.ToList();
        if (!story)
            clips.AddRange(context.ProtectedContext.Where(range => Valid(range, duration))
                .Select(range => new DraftClip(range.Start, range.End, [], "Preserved scene context: " + range.Reason)));
        clips = Union(clips);
        var before = clips.Sum(clip => clip.Duration);
        var cuts = trimQuiet ? context.QuietDowntime.Where(range => Valid(range, duration) && range.End - range.Start >= 2).ToArray() : [];
        foreach (var cut in cuts)
        {
            var safeCuts = new List<(double Start, double End)> { (cut.Start, cut.End) };
            foreach (var protect in protectedRanges)
                safeCuts = safeCuts.SelectMany(part => Subtract(part.Start, part.End, protect.Start, protect.End)).ToList();
            foreach (var safe in safeCuts.Where(part => part.End - part.Start >= 2))
                clips = clips.SelectMany(clip => Subtract(clip.StartSeconds, clip.EndSeconds, safe.Start, safe.End)
                    .Select(part => clip with { StartSeconds = part.Start, EndSeconds = part.End })).ToList();
        }
        return highlightPlan with { Clips = clips, RemovedQuietSeconds = before - clips.Sum(clip => clip.Duration),
            ProtectedContextCount = context.ProtectedContext.Count(range => Valid(range, duration)), StoryContinuity = story };
    }

    private static IEnumerable<(double Start, double End)> Subtract(double start, double end, double cutStart, double cutEnd)
    {
        if (cutEnd <= start || cutStart >= end) { yield return (start, end); yield break; }
        if (cutStart > start) yield return (start, Math.Min(end, cutStart));
        if (cutEnd < end) yield return (Math.Max(start, cutEnd), end);
    }

    private static List<DraftClip> Union(IEnumerable<DraftClip> source)
    {
        var result = new List<DraftClip>();
        foreach (var clip in source.OrderBy(clip => clip.StartSeconds))
        {
            if (result.LastOrDefault() is { } previous && clip.StartSeconds <= previous.EndSeconds)
                result[^1] = previous with { EndSeconds = Math.Max(previous.EndSeconds, clip.EndSeconds),
                    BookmarkIds = previous.BookmarkIds.Concat(clip.BookmarkIds).Distinct().ToArray(), Description = previous.Description + " / " + clip.Description };
            else result.Add(clip);
        }
        return result;
    }
}
