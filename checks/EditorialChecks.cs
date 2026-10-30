using System.IO;
using System.Text;
using ReelGood;

internal static class EditorialChecks
{
    public static void Run()
    {
        var empty = DraftEditPlanner.Build([], 60);
        var story = EditorialPolicy.Apply(empty, [], 60, new(EditorialMode.Story, [], []), true);
        if (story.Clips.Count != 1 || story.Clips[0].StartSeconds != 0 || story.Clips[0].EndSeconds != 60 || story.RemovedQuietSeconds != 0)
            throw new Exception("Unreviewed story footage was discarded");
        var context = new DraftReviewContext(EditorialMode.Story, [new(25, 35, "Character dialogue")], [new(20, 30, "Reviewed quiet waiting")]);
        story = EditorialPolicy.Apply(empty, [], 60, context, true);
        if (story.RemovedQuietSeconds != 5 || story.Clips.Count != 2 || story.Clips[1].StartSeconds != 25)
            throw new Exception("Scene context was cut from quiet downtime");
        DraftBookmark[] keep = [new(0, 24, 27, "Joke setup", "Keep")];
        story = EditorialPolicy.Apply(empty, keep, 60, new(EditorialMode.Story, [], [new(20, 30, "Quiet")]), true);
        if (story.RemovedQuietSeconds != 7 || !story.Clips.Any(clip => clip.StartSeconds <= 24 && clip.EndSeconds >= 27))
            throw new Exception("Explicit Keep did not protect quiet joke setup");
        var verified = EditorialPolicy.Apply(empty, [new(0, 24, 27, "Verified elimination", null, false, true)], 60, new(EditorialMode.Story, [], [new(20, 30, "Quiet")]), true);
        if (verified.RemovedQuietSeconds != 7 || !verified.Clips.Any(clip => clip.StartSeconds <= 24 && clip.EndSeconds >= 27))
            throw new Exception("Quiet cleanup discarded a verified detector outcome");
        var highlights = DraftEditPlanner.Build([new(0, 10, 15, "Elimination", null)], 60);
        var montage = EditorialPolicy.Apply(highlights, [], 60, context with { Mode = EditorialMode.Highlights }, true);
        if (montage.Clips.Count != 2 || !montage.Clips.Any(clip => clip.StartSeconds == 25 && clip.EndSeconds == 35))
            throw new Exception("Highlights lost identified scene context");
        var noTrim = EditorialPolicy.Apply(empty, [], 60, context, false);
        if (noTrim.RemovedQuietSeconds != 0 || noTrim.Clips[0].Duration != 60) throw new Exception("Disabling cleanup did not preserve the source");
        var invalid = EditorialPolicy.Apply(empty, [], 60, new(EditorialMode.Story, [], [new(double.NaN, 50, "bad"), new(60, 80, "out of source")]), true);
        if (invalid.RemovedQuietSeconds != 0) throw new Exception("Invalid quiet cut was applied");
        Console.WriteLine("PASS Story retains unreviewed footage; cutscenes/dialogue and explicit Keeps protect ranges; highlights retain identified context; quiet cleanup can be disabled");

        var folder = @"D:\Editor\ReelGood\checks\results\editorial"; Directory.CreateDirectory(folder);
        var silent = Path.Combine(folder, "quiet.wav"); var speech = Path.Combine(folder, "voice-activity.wav");
        void Wave(string path, bool activity)
        {
            const int samples = 12 * 16000;
            using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (var i = 0; i < samples; i++) writer.Write((short)(activity && i >= 5 * 16000 && i < 7 * 16000 ? 5000 : 0));
        }
        Wave(silent, false); Wave(speech, true);
        var quiet = QuietAudioDetector.Find([silent], 12);
        if (quiet.Count != 1 || quiet[0].Start != 1 || quiet[0].End != 11) throw new Exception("Quiet scan failed to preserve edge handles");
        var separate = QuietAudioDetector.Find([silent, speech], 12);
        if (separate.Count != 2 || separate.Any(range => range.Start < 7 && range.End > 5)) throw new Exception("Activity on the second audio track was cut");
        if (QuietAudioDetector.Find([], 12).Count != 0) throw new Exception("Missing audio was treated as silence");
        Console.WriteLine("PASS Quiet analysis checks both independent tracks, protects activity and edge handles, and refuses missing audio");
        if (!EditorialPolicy.ScanQuery("", EditorialMode.Story).Contains("dialogue") ||
            !EditorialPolicy.ScanQuery("", EditorialMode.Highlights).Contains("eliminations") ||
            !EditorialPolicy.ScanQuery("player jokes", EditorialMode.Highlights).Contains("player jokes")) throw new Exception("Prompt and mode objectives were not combined");
        var fortnite = EditorialPolicy.PreferenceScope("Fortnite", "Zero Build", false, EditorialMode.Highlights);
        if (fortnite != EditorialPolicy.PreferenceScope(" FORTNITE ", "zero   build", false, EditorialMode.Highlights) ||
            fortnite == EditorialPolicy.PreferenceScope("Fortnite", "Ranked", false, EditorialMode.Highlights) ||
            fortnite == EditorialPolicy.PreferenceScope("Gears", "Campaign", false, EditorialMode.Story) ||
            EditorialPolicy.PreferenceScope("Fortnite", "Zero Build", true, EditorialMode.Story) != EditorialPolicy.PreferenceScope("Gears", "Campaign", true, EditorialMode.Story))
            throw new Exception("Game/mode preferences leaked across contexts or IRL reused game metadata");
        Console.WriteLine("PASS Game and game-mode feedback scopes are distinct, normalized, and ignored for IRL");
        foreach (var pair in new[] { (EditorialMode.Highlights, EditorialMode.Comedy), (EditorialMode.Story, EditorialMode.Comedy), (EditorialMode.Story, EditorialMode.Highlights) })
        {
            var forward = context with { Mode = pair.Item1, SecondaryMode = pair.Item2 };
            var reverse = context with { Mode = pair.Item2, SecondaryMode = pair.Item1 };
            var a = EditorialPolicy.Apply(highlights, [], 60, forward, false);
            var b = EditorialPolicy.Apply(highlights, [], 60, reverse, false);
            if (a.StoryContinuity != (pair.Item1 == EditorialMode.Story || pair.Item2 == EditorialMode.Story) ||
                !a.Clips.Select(clip => (clip.StartSeconds, clip.EndSeconds)).SequenceEqual(b.Clips.Select(clip => (clip.StartSeconds, clip.EndSeconds))))
                throw new Exception("Combined modes depend on slot order or fail to preserve story");
            var combined = EditorialPolicy.ScanQuery("", pair.Item1, pair.Item2);
            foreach (var target in EditorialPolicy.ScanQuery("", pair.Item1).Split(", ").Concat(EditorialPolicy.ScanQuery("", pair.Item2).Split(", ")))
                if (!combined.Contains(target)) throw new Exception("One selected mode was missing from scan objectives");
            if (EditorialPolicy.PreferenceScope("game", "mode", false, pair.Item1, pair.Item2) != EditorialPolicy.PreferenceScope("game", "mode", false, pair.Item2, pair.Item1))
                throw new Exception("Swapping slots lost the same combined-mode preference profile");
        }
        if (EditorialPolicy.PreferenceScope("Fortnite", "Zero Build", false, EditorialMode.Highlights, EditorialMode.Comedy) == fortnite ||
            EditorialPolicy.ScanQuery("", EditorialMode.Comedy, EditorialMode.Comedy) != EditorialPolicy.ScanQuery("", EditorialMode.Comedy) ||
            !EditorialPolicy.ScanQuery("eliminations", EditorialMode.Story, EditorialMode.Comedy).Contains("laughter"))
            throw new Exception("Secondary modes duplicated goals, reused single-mode feedback or lost a combined prompt");
        var combinedRequest = EditorialPolicy.ScanQuery("laughter; clutch saves", EditorialMode.Highlights, EditorialMode.Comedy);
        if (!combinedRequest.Contains("clutch saves") || !combinedRequest.Contains("eliminations") || combinedRequest.Split(", ").Count(target => target == "laughter") != 1)
            throw new Exception("Combining prompt/modes lost a custom target or duplicated a shared target");
        Console.WriteLine("PASS All three mode pairs combine objectives; Story wins in either slot; swapped pairs share a profile; None and duplicate selections preserve single-mode behavior");
    }
}
