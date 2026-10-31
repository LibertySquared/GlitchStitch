using System.Text.Json;
using GlitchStich;

internal static class CopilotChecks
{
    public static void Run()
    {
        foreach (var empty in new[] { "{}", "{\"message\":\"Review these edits\",\"prompt\":null,\"bookmarks\":[]}" })
        {
            try { CopilotProposal.Parse(empty); throw new Exception("An empty review was presented as successful"); }
            catch (JsonException) { }
        }
        var specific = CopilotProposal.Parse("""{"message":"The reviewed window shows walking rather than an elimination, so exclude that candidate.","prompt":null,"bookmarks":[]}""");
        if (specific.Proposals.Count != 0 || !specific.Message.Contains("walking")) throw new Exception("A substantive no-change reply was lost");
        var alias = CopilotProposal.Parse("""{"explanation":"The reaction starts late; extend the setup by two seconds.","prompt":"clear group laughter","bookmarks":[]}""");
        var leadIn = CopilotProposal.Parse("""{"message":"Preserve four seconds before the laughter event.","prompt":null,"bookmarks":[{"id":0,"start":6,"end":20,"eventTime":15,"leadInSeconds":4,"decision":null}]}""");
        if (leadIn.Proposals.Single().Start != 11) throw new Exception("Relative context trusted incorrect model arithmetic");
        if (alias.Proposals.Count != 1 || !alias.Message.Contains("reaction")) throw new Exception("A useful alternate explanation was hidden");
        var verdict = FootageVerdict.Parse("""{"verdict":"KEEP","reason":"An elimination notification appears after the shot.","observations":"The sampled frame shows ELIMINATED and audio has a reaction.","event_offset":6,"start_offset":2,"end_offset":10}""", 12);
        if (verdict.EventOffset != 6 || verdict.StartOffset != 2) throw new Exception("Valid relative review timestamps changed");
        var disagreement = verdict with { Verdict = "SKIP", ContextType = "Downtime" };
        if (disagreement.PreserveVerifiedOutcome("Video frames", "Fortnite · Elimination").Verdict != "UNCERTAIN" ||
            disagreement.PreserveVerifiedOutcome("Video frames", "Closest video candidate: Elimination").Verdict != "SKIP")
            throw new Exception("A model disagreement could reject a verified outcome or elevate a weak fallback");
        var invalid = FootageVerdict.Parse("""{"verdict":"UNCERTAIN","reason":"Timing is not clear.","observations":"No readable notification appears.","event_offset":999,"start_offset":8,"end_offset":3}""", 12);
        if (invalid.EventOffset is not null || invalid.StartOffset is not null || invalid.EndOffset is not null) throw new Exception("Invalid review offsets were accepted");
        var plan = FootageReviewPlanner.Build([new(1, 30, "Laughter"), new(2, 31, "Duplicate"), new(3, 90, "Elimination")], 300, 12);
        if (plan.Count > 12 || plan.Count(window => window.BookmarkId is not null) != 2 || !plan.Any(window => window.BookmarkId is null)) throw new Exception("Review coverage failed to deduplicate or scout missing candidates");
        if (FootageReviewPlanner.Build([], 300, 6).Count != 6 || FootageReviewPlanner.Build([], double.NaN, 6).Count != 0) throw new Exception("Zero-detection footage scouting failed");
        Console.WriteLine("PASS Copilot rejects empty reviews, preserves useful explanations, validates evidence/timestamps, and scouts beyond detector hits with bounded coverage");
    }
}
