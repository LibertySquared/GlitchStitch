using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ReelGood;

public partial class MainWindow
{
    private string ApplyCopilotProposal(CopilotProposal proposal, string snapshot)
    {
        if (_busyOperations > 0 || _isClosing) return "Wait for the current operation to finish, then review again.";
        if (snapshot != BuildCopilotContext(false)) return "The project changed since this suggestion. Ask Glitchy for a fresh review.";
        var issue = proposal.Validate(_durationSeconds, _cues.Count);
        if (issue is not null) return issue;
        // Persist a correction before changing the project; failure leaves the edit unapplied.
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var previous = proposal.BookmarkId is int id ? _cues[id] : null;
            var correction = new
            {
                approvedAt = DateTimeOffset.Now, source = _videoPath, prompt = PreferenceBox.Text,
                scope = CurrentPreferenceScope, game = GameTitleBox.Text, gameMode = GameModeBox.Text,
                previous = previous is null ? null : new { previous.TimeSeconds, previous.EventTimeSeconds, previous.ClipEndSeconds, previous.EventLabel, previous.Decision },
                applied = proposal,
                note = "User-approved Glitchy correction; preference feedback, not detector weight training"
            };
            File.AppendAllText(Path.Combine(_settingsDirectory, "copilot-corrections.jsonl"), JsonSerializer.Serialize(correction) + Environment.NewLine);
            if (proposal.Prompt is not null) PreferenceBox.Text = proposal.Prompt.Trim();
            else
            {
                var index = proposal.BookmarkId!.Value; var old = _cues[index];
                var decision = proposal.Decision ?? old.Decision;
                if (old.Decision != decision)
                {
                    if (old.Decision is not null) AddEventFeedback(old.EventLabel, old.Decision, -1, old.FeedbackScope);
                    if (decision is not null) AddEventFeedback(old.EventLabel, decision, 1);
                }
                var updated = new CueItem(proposal.Start!.Value, old.EventLabel, old.TrackLabel, proposal.EventTime ?? old.EventTimeSeconds,
                    old.Confidence, old.AdjustedConfidence, proposal.End!.Value) { Decision = decision, AiSuggestion = "User-approved Glitchy boundary correction", FeedbackScope = old.Decision == decision ? old.FeedbackScope : CurrentPreferenceScope };
                _cues[index] = updated;
                SaveFeedback();
                RefreshScanCategories(PreferenceBox.Text.Trim()); BuildTimeline();
                _clipStartSeconds = updated.TimeSeconds; _clipEndSeconds = updated.ClipEndSeconds;
                SelectCueForClip(updated);
            }
            MarkProjectChanged();
            StatusText.Text = proposal.Prompt is not null ? "Glitchy prompt applied · press Start scan when ready" : "Glitchy bookmark correction applied and saved as feedback";
            return "Applied. Saved locally; no rescan or timeline export was started.";
        }
        catch (Exception ex) { return $"Could not save the correction: {ex.Message}"; }
    }
}

