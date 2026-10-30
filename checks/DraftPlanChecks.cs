using ReelGood;

internal static class DraftPlanChecks
{
    public static void Run()
    {
        void Check(bool valid, string label) { if (!valid) throw new Exception(label); Console.WriteLine("PASS " + label); }
        DraftBookmark[] bookmarks = [
            new(0, 10, 17, "Laughter", "Keep"), new(1, 14, 20, "Elimination", null),
            new(2, 50, 53, "Unwanted sound", "Skip"), new(3, 30, 34, "Closest frame", null, true),
            new(4, 70, 75, "Approved fallback", "Keep", true), new(5, 16, 18, "Skipped duplicate", "Skip")];
        var plan = DraftEditPlanner.Build(bookmarks, 90);
        Check(plan.Clips.Count == 2 && plan.Clips[0].StartSeconds == 10 && plan.Clips[0].EndSeconds == 21 && plan.Clips[1].StartSeconds == 70 && plan.Clips[1].EndSeconds == 76, "Draft merges audio/video overlaps, adds end padding, and retains source order");
        Check(plan.IncludedBookmarks == 3 && plan.SkippedBookmarks == 2 && plan.WeakCandidatesExcluded == 1 && plan.SkippedOverlaps == 1, "Skipped and unapproved weak matches do not contribute; conflicting skipped overlaps are reported");
        var kept = DraftEditPlanner.Build(bookmarks, 90, false, 0, 0);
        Check(kept.Clips.Count == 2 && kept.Clips[0].EndSeconds == 17 && kept.UnreviewedExcluded == 1, "Kept-only mode preserves explicit Keep and exact boundaries with zero padding");
        var edges = DraftEditPlanner.Build([new(0, -2, 2, "Start", null), new(1, 8, 15, "End", null), new(2, 4, 3, "Reversed", null), new(3, double.NaN, 5, "Invalid", null), new(4, 12, 14, "Beyond", null)], 10, true, 0, 0);
        Check(edges.Clips.Count == 2 && edges.Clips[0].StartSeconds == 0 && edges.Clips[1].EndSeconds == 10 && edges.InvalidBookmarks == 3, "Draft clamps source edges and rejects reversed, non-finite, and out-of-source ranges");
        var nearby = DraftEditPlanner.Build([new(0, 5, 7, "One", null), new(1, 7.75, 9, "Two", null), new(2, 11, 13, "Three", null)], 20, true, 1, 0);
        Check(nearby.Clips.Count == 2 && nearby.Clips[0].BookmarkIds.SequenceEqual(new[] { 0, 1 }), "Nearby moments merge only within the chosen gap");
        Check(DraftEditPlanner.Build([new(0, 1, 3, "Skip", "Skip")], 10).Clips.Count == 0, "All-skipped and empty drafts stay empty without fabricated selections");
        foreach (var invalid in new[] { double.NaN, -1, 11 })
        {
            try { DraftEditPlanner.Build([], 10, true, invalid); throw new Exception("Invalid gap accepted"); }
            catch (ArgumentOutOfRangeException) { }
        }
        Check(true, "Invalid draft settings are rejected");
    }
}
