namespace GlitchStich;

internal sealed record DraftBookmark(int Id, double Start, double End, string Label, string? Decision, bool IsWeakCandidate = false, bool IsVerifiedOutcome = false);
internal sealed record DraftClip(double StartSeconds, double EndSeconds, IReadOnlyList<int> BookmarkIds, string Description)
{
    public double Duration => EndSeconds - StartSeconds;
}
internal sealed record DraftEditPlan(IReadOnlyList<DraftClip> Clips, int IncludedBookmarks, int SkippedBookmarks,
    int UnreviewedExcluded, int WeakCandidatesExcluded, int InvalidBookmarks, int SkippedOverlaps)
{
    public double RemovedQuietSeconds { get; init; }
    public int ProtectedContextCount { get; init; }
    public bool StoryContinuity { get; init; }
}

internal static class DraftEditPlanner
{
    public static DraftEditPlan Build(IEnumerable<DraftBookmark> bookmarks, double sourceDuration,
        bool includeUnreviewed = true, double mergeGap = 1, double endPadding = 1)
    {
        if (!double.IsFinite(sourceDuration) || sourceDuration <= 0) throw new ArgumentOutOfRangeException(nameof(sourceDuration));
        if (!double.IsFinite(mergeGap) || mergeGap < 0 || mergeGap > 10) throw new ArgumentOutOfRangeException(nameof(mergeGap));
        if (!double.IsFinite(endPadding) || endPadding < 0 || endPadding > 10) throw new ArgumentOutOfRangeException(nameof(endPadding));
        var selected = new List<(DraftBookmark Bookmark, double Start, double End)>();
        var skipped = new List<DraftBookmark>();
        int unreviewed = 0, weak = 0, invalid = 0;
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.Decision == "Skip") { skipped.Add(bookmark); continue; }
            if (bookmark.IsWeakCandidate && bookmark.Decision != "Keep") { weak++; continue; }
            if (!includeUnreviewed && bookmark.Decision != "Keep") { unreviewed++; continue; }
            if (!double.IsFinite(bookmark.Start) || !double.IsFinite(bookmark.End) || bookmark.End <= bookmark.Start)
            { invalid++; continue; }
            var start = Math.Clamp(bookmark.Start, 0, sourceDuration);
            var end = Math.Clamp(bookmark.End + endPadding, 0, sourceDuration);
            if (end - start < .25) { invalid++; continue; }
            selected.Add((bookmark, start, end));
        }
        var clips = new List<DraftClip>();
        foreach (var item in selected.OrderBy(item => item.Start).ThenBy(item => item.End))
        {
            var previous = clips.LastOrDefault();
            if (previous is not null && item.Start <= previous.EndSeconds + mergeGap)
            {
                var ids = previous.BookmarkIds.Append(item.Bookmark.Id).Distinct().ToArray();
                var labels = previous.Description.Split(" / ").Append(item.Bookmark.Label).Distinct();
                clips[^1] = new(previous.StartSeconds, Math.Max(previous.EndSeconds, item.End), ids, string.Join(" / ", labels));
            }
            else clips.Add(new(item.Start, item.End, [item.Bookmark.Id], item.Bookmark.Label));
        }
        var overlaps = skipped.Count(bookmark => double.IsFinite(bookmark.Start) && double.IsFinite(bookmark.End) &&
            clips.Any(clip => bookmark.Start < clip.EndSeconds && bookmark.End > clip.StartSeconds));
        return new(clips, selected.Count, skipped.Count, unreviewed, weak, invalid, overlaps);
    }
}
