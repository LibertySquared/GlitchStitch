using System.IO;

namespace ReelGood;

internal sealed record ClipEffects(double Speed = 1, int Rotation = 0, double CropLeft = 0, double CropTop = 0,
    double CropRight = 0, double CropBottom = 0, double Volume = 1, double FadeIn = 0, double FadeOut = 0,
    double Brightness = 0, double Contrast = 1)
{
    public string? Validate() => !double.IsFinite(Speed) || Speed < .25 || Speed > 4 ? "Speed must be between 0.25× and 4×." :
        Rotation is not (0 or 90 or 180 or 270) ? "Rotation must be 0, 90, 180 or 270 degrees." :
        new[] { CropLeft, CropTop, CropRight, CropBottom }.Any(value => !double.IsFinite(value) || value < 0 || value > .45) ? "Crop values must be from 0% to 45% per edge." :
        !double.IsFinite(Volume) || Volume < 0 || Volume > 2 ? "Glitch volume must be from 0% to 200%." :
        !double.IsFinite(FadeIn) || !double.IsFinite(FadeOut) || FadeIn < 0 || FadeIn > 5 || FadeOut < 0 || FadeOut > 5 ? "Fades must be between 0 and 5 seconds." :
        !double.IsFinite(Brightness) || Brightness < -1 || Brightness > 1 || !double.IsFinite(Contrast) || Contrast < .5 || Contrast > 2 ? "Brightness must be from -1 to 1, contrast from 0.5 to 2." : null;
}

internal sealed record EditingClip(double Start, double End, int Layer, ClipEffects Effects)
{
    public double Duration => (End - Start) / Effects.Speed;
    public string Display => $"{Start:0.##}–{End:0.##}s · {Duration:0.##}s stitched · {Effects.Speed:0.##}×";
    public string? Validate(double duration) => !double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End > duration || End - Start < .25
        ? "Choose a source range inside the video, at least 0.25 seconds long." : Effects.Validate();
}

internal sealed record EditingAsset(string Id, string Path, string Kind, string Description, bool AllowAi = false, bool HasAudio = false, double Duration = 0)
{
    public IReadOnlyList<int> AudioStreams { get; init; } = [];
    public string Display => $"{System.IO.Path.GetFileName(Path)} · {Kind} · {(AllowAi ? "AI allowed" : "manual only")} · {Description}";
}

internal sealed record TimedMedia(string Id, string? AssetId, string Kind, double SourceAnchor, double Duration,
    double X = .6, double Y = .6, double Size = .35, double Volume = 1, string Text = "", double AssetOffset = 0)
{
    public int Occurrence { get; init; }
    public string Display => $"{Kind} at source {SourceAnchor:0.##}s · {Duration:0.##}s · {(Kind == "Text" ? Text : AssetId)}";
    public string? Validate(double sourceDuration, IReadOnlyList<EditingAsset> assets) =>
        Kind is not ("Image" or "Video" or "Audio" or "Text") ? "Unsupported media type." :
        !double.IsFinite(SourceAnchor) || SourceAnchor < 0 || SourceAnchor >= sourceDuration || !double.IsFinite(Duration) || Duration < .1 || Duration > 600 ? "Choose a valid anchor and duration (0.1–600 seconds)." :
        new[] { X, Y, Size, Volume, AssetOffset }.Any(value => !double.IsFinite(value)) || X < 0 || X > 1 || Y < 0 || Y > 1 || Size < .05 || Size > 1 || Volume < 0 || Volume > 2 || AssetOffset < 0 || Occurrence < 0 || Occurrence > 1000 ? "Position/size must fit the frame, volume must be 0–200%, and the source occurrence must be valid." :
        Kind == "Text" ? string.IsNullOrWhiteSpace(Text) || Text.Length > 2000 ? "Enter 1–2,000 characters of caption text." : null :
        !assets.Any(asset => asset.Id == AssetId && asset.Kind == Kind && File.Exists(asset.Path)) ? "The media asset is missing or no longer matches this placement." :
        assets.Any(asset => asset.Id == AssetId && asset.Duration > 0 && asset.Kind != "Image" && AssetOffset >= asset.Duration) ? "The asset offset is past the end of this media file." : null;
}

internal sealed record EditingTrack(int Stream, string Label, string Codec, int Channels, string? Wav, string? External,
    double Volume, bool Muted, bool Combined, string? DuplicateOf, string? Fingerprint);
internal sealed record EditingSnapshot(IReadOnlyList<EditingClip> Clips, IReadOnlyList<EditingAsset> Assets, IReadOnlyList<TimedMedia> Media)
{
    public IReadOnlyList<EditingTrack> Audio { get; init; } = [];
    public IReadOnlyList<int> RemovedAudio { get; init; } = [];
}

internal sealed class EditHistory
{
    private readonly Stack<EditingSnapshot> _undo = new(), _redo = new();
    public void Push(EditingSnapshot prior) { _undo.Push(prior); _redo.Clear(); if (_undo.Count > 80) { var keep = _undo.Take(80).Reverse().ToArray(); _undo.Clear(); foreach (var item in keep) _undo.Push(item); } }
    public EditingSnapshot? Undo(EditingSnapshot current) { if (_undo.Count == 0) return null; _redo.Push(current); return _undo.Pop(); }
    public EditingSnapshot? Redo(EditingSnapshot current) { if (_redo.Count == 0) return null; _undo.Push(current); return _redo.Pop(); }
    public void Clear() { _undo.Clear(); _redo.Clear(); }
}

internal static class EditTimeMap
{
    public static double? ToOutput(double sourceTime, IReadOnlyList<EditingClip> clips, int occurrence = 0)
    {
        double cursor = 0;
        foreach (var clip in clips)
        {
            if (sourceTime >= clip.Start && sourceTime < clip.End) { if (occurrence-- == 0) return cursor + (sourceTime - clip.Start) / clip.Effects.Speed; }
            cursor += clip.Duration;
        }
        return null;
    }
    public static bool ReviewedAndUnprotected(double sourceAnchor, double duration, IReadOnlyList<EditingClip> clips,
        IReadOnlyList<EditorialRange> reviewed, IReadOnlyList<EditorialRange> protectedContext, int occurrence = 0)
    {
        var outputStart = ToOutput(sourceAnchor, clips, occurrence); if (outputStart is null) return false;
        var outputEnd = outputStart.Value + duration; double cursor = 0; bool coveredAny = false;
        foreach (var clip in clips)
        {
            var a = Math.Max(outputStart.Value, cursor); var b = Math.Min(outputEnd, cursor + clip.Duration);
            if (b > a)
            {
                coveredAny = true; var sourceStart = clip.Start + (a - cursor) * clip.Effects.Speed; var sourceEnd = clip.Start + (b - cursor) * clip.Effects.Speed;
                if (protectedContext.Any(range => sourceStart < range.End && sourceEnd > range.Start)) return false;
                var covered = sourceStart;
                foreach (var range in reviewed.OrderBy(range => range.Start)) { if (range.Start > covered + .01) break; if (range.End > covered) covered = range.End; if (covered >= sourceEnd - .01) break; }
                if (covered < sourceEnd - .01) return false;
            }
            cursor += clip.Duration;
        }
        return coveredAny;
    }
}
