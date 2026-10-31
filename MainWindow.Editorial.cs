using System.Windows;
using System.Windows.Controls;

namespace GlitchStich;

public partial class MainWindow
{
    private readonly List<EditorialRange> _protectedSceneRanges = [];
    private readonly List<EditorialRange> _quietDowntimeRanges = [];
    private EditorialMode CurrentEditorialMode => EditModeBox?.SelectedIndex switch
    { 1 => EditorialMode.Highlights, 2 => EditorialMode.Comedy, _ => EditorialMode.Story };
    private EditorialMode? CurrentSecondaryEditorialMode => SecondaryEditModeBox?.SelectedIndex switch
    { 1 => EditorialMode.Story, 2 => EditorialMode.Highlights, 3 => EditorialMode.Comedy, _ => null };
    private string CurrentPreferenceScope => EditorialPolicy.PreferenceScope(GameTitleBox?.Text, GameModeBox?.Text, IRLModeCheck?.IsChecked == true, CurrentEditorialMode, CurrentSecondaryEditorialMode);
    private string DeclaredFootageContext => IRLModeCheck?.IsChecked == true ? "IRL / real-life footage" :
        $"Game: {GameTitleBox?.Text.Trim()}; mode: {GameModeBox?.Text.Trim()} (user-provided context, not proof of an event)";
    private IEnumerable<KeyValuePair<string, EventPreference>> ScopedEventPreferences => _eventPreferences.Where(pair => pair.Key.StartsWith(CurrentPreferenceScope + "::", StringComparison.OrdinalIgnoreCase));

    private void GameModeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GameModePlaceholder is not null) GameModePlaceholder.Visibility = string.IsNullOrEmpty(GameModeBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_videoPath is not null) { if (_busyOperations == 0) _quietDowntimeRanges.Clear(); MarkProjectChanged(); }
    }

    private IEnumerable<string> ScopedCorrections(string path)
    {
        foreach (var line in System.IO.File.ReadLines(path).TakeLast(100))
        {
            bool matches;
            try
            {
                using var item = System.Text.Json.JsonDocument.Parse(line);
                matches = item.RootElement.TryGetProperty("scope", out var scope) && scope.GetString() == CurrentPreferenceScope;
            }
            catch { continue; }
            if (matches) yield return line;
        }
    }

    private void EditMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SecondaryEditModeBox is not null && CurrentSecondaryEditorialMode == CurrentEditorialMode) SecondaryEditModeBox.SelectedIndex = 0;
        if (PreferenceStatus is null) return;
        PreferenceStatus.Text = EditorialPolicy.HasStory(CurrentEditorialMode, CurrentSecondaryEditorialMode)
            ? "Story mode retains scene continuity; only reviewed quiet downtime is proposed for removal."
            : "Highlight/comedy drafts select moments and preserve identified character dialogue and cutscenes. Review before applying.";
        if (_videoPath is not null) MarkProjectChanged();
    }

    private void RecordEditorialEvidence(FootageVerdict verdict, double start, double length)
    {
        _quietDowntimeRanges.RemoveAll(range => range.Start < start + length && range.End > start);
        if (verdict.ContextType is "Cutscene" or "CharacterDialogue")
        {
            var range = new EditorialRange(Math.Max(0, start - 3), Math.Min(_durationSeconds, start + length + 3),
                verdict.ContextType + ": " + verdict.Observations);
            if (!_protectedSceneRanges.Any(old => Math.Abs(old.Start - range.Start) < 1 && Math.Abs(old.End - range.End) < 1))
                _protectedSceneRanges.Add(range);
        }
        else if (verdict.ContextType == "Downtime" && verdict.Verdict == "SKIP" && verdict.AudioTracksSupplied > 0)
        {
            foreach (var quiet in verdict.QuietAudioRanges)
            {
                var range = new EditorialRange(start + quiet.Start, start + quiet.End, "Reviewed inactive footage + quiet audio: " + verdict.Reason);
                if (EditorialPolicy.Valid(range, _durationSeconds) && !_quietDowntimeRanges.Any(old => Math.Abs(old.Start - range.Start) < 1 && Math.Abs(old.End - range.End) < 1))
                    _quietDowntimeRanges.Add(range);
            }
        }
        MarkProjectChanged();
    }
}
