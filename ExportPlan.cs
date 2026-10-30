using System.Globalization;

namespace ReelGood;

internal sealed record ExportSegment(double Start, double End)
{
    public ClipEffects? Effects { get; init; }
    public double Duration => (End - Start) / (Effects?.Speed ?? 1);
}
internal sealed record ExportAudio(int StreamIndex, string? ExternalWav, double Volume, bool Muted);

internal static class ExportPlan
{
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static List<string> Build(string source, string destination, IReadOnlyList<ExportSegment> segments, IReadOnlyList<ExportAudio> tracks, ExportOptions? options = null)
    {
        if (segments.Count == 0 || segments.Any(s => !double.IsFinite(s.Start) || !double.IsFinite(s.End) || s.Start < 0 || s.End <= s.Start))
            throw new ArgumentException("Choose a valid video range to export.");
        if (string.Equals(System.IO.Path.GetFullPath(source), System.IO.Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Export to a different file from the source video.");
        options ??= new ExportOptions();
        if (segments.Any(segment => segment.Effects?.Validate() is not null)) throw new ArgumentException("A glitch has invalid effect settings.");
        if (options.MergeAudio) tracks = tracks.Where(track => !track.Muted && track.Volume > 0).ToArray();
        var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error" };
        var filter = new List<string>();
        var concat = new List<string>();
        var nextInput = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var input = nextInput++;
            var duration = Number(segment.End - segment.Start);
            var outputDuration = Number(segment.Duration);
            var effects = segment.Effects ?? new ClipEffects();
            if (options.HardwareDecode && options.Encoder == "h264_nvenc") args.AddRange(["-hwaccel", "cuda"]);
            args.AddRange(["-ss", Number(segment.Start), "-t", duration, "-threads", Math.Clamp(options.Threads / 2, 1, 2).ToString(), "-i", source]);
            var sizing = options.Quality == "Balanced" ? ",scale=w='min(1920,iw)':h='min(1080,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2,fps='min(source_fps,60)'" : "";
            var videoFx = "";
            if (effects.Speed != 1) videoFx += $",setpts=PTS/{Number(effects.Speed)}";
            if (effects.CropLeft + effects.CropRight + effects.CropTop + effects.CropBottom > 0)
                videoFx += $",crop=w='trunc(iw*{Number(1 - effects.CropLeft - effects.CropRight)}/2)*2':h='trunc(ih*{Number(1 - effects.CropTop - effects.CropBottom)}/2)*2':x='trunc(iw*{Number(effects.CropLeft)}/2)*2':y='trunc(ih*{Number(effects.CropTop)}/2)*2'";
            videoFx += effects.Rotation switch { 90 => ",transpose=1", 180 => ",hflip,vflip", 270 => ",transpose=2", _ => "" };
            if (effects.Brightness != 0 || effects.Contrast != 1) videoFx += $",eq=brightness={Number(effects.Brightness)}:contrast={Number(effects.Contrast)}";
            if (options.CanvasWidth > 0 && options.CanvasHeight > 0)
                videoFx += $",scale={options.CanvasWidth}:{options.CanvasHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={options.CanvasWidth}:{options.CanvasHeight}:(ow-iw)/2:(oh-ih)/2,setsar=1";
            var fadeIn = Math.Min(effects.FadeIn, segment.Duration / 2); var fadeOut = Math.Min(effects.FadeOut, segment.Duration / 2);
            if (fadeIn > 0) videoFx += $",fade=t=in:st=0:d={Number(fadeIn)}";
            if (fadeOut > 0) videoFx += $",fade=t=out:st={Number(segment.Duration - fadeOut)}:d={Number(fadeOut)}";
            filter.Add($"[{input}:v:0]setpts=PTS-STARTPTS,trim=duration={duration}{videoFx}{sizing},format={(options.Encoder is "h264_qsv" or "h264_nvenc" ? "nv12" : "yuv420p")}[v{i}]");
            concat.Add($"[v{i}]");
            for (var j = 0; j < tracks.Count; j++)
            {
                var track = tracks[j];
                var audioInput = input;
                var specifier = track.StreamIndex.ToString(CultureInfo.InvariantCulture);
                if (track.ExternalWav is not null)
                {
                    audioInput = nextInput++;
                    args.AddRange(["-ss", Number(segment.Start), "-t", duration, "-i", track.ExternalWav]);
                    specifier = "a:0";
                }
                var volume = track.Muted ? 0 : Math.Clamp(track.Volume, 0, 1);
                var tempo = effects.Speed; var audioFx = "";
                while (tempo > 2) { audioFx += ",atempo=2"; tempo /= 2; }
                while (tempo < .5) { audioFx += ",atempo=0.5"; tempo /= .5; }
                if (tempo != 1) audioFx += $",atempo={Number(tempo)}";
                if (fadeIn > 0) audioFx += $",afade=t=in:st=0:d={Number(fadeIn)}";
                if (fadeOut > 0) audioFx += $",afade=t=out:st={Number(segment.Duration - fadeOut)}:d={Number(fadeOut)}";
                filter.Add($"[{audioInput}:{specifier}]asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo,volume={Number(volume * effects.Volume)},apad,atrim=duration={duration}{audioFx},apad,atrim=duration={outputDuration}[a{i}_{j}]");
                if (!options.MergeAudio) concat.Add($"[a{i}_{j}]");
            }
            if (options.MergeAudio && tracks.Count > 0)
            {
                var inputs = string.Concat(Enumerable.Range(0, tracks.Count).Select(j => $"[a{i}_{j}]"));
                filter.Add($"{inputs}amix=inputs={tracks.Count}:normalize=0:duration=longest,alimiter=limit=0.95:level=0:latency=1,apad,atrim=duration={outputDuration}[mixed{i}]");
                concat.Add($"[mixed{i}]");
            }
        }
        var audioOutputs = options.MergeAudio ? Math.Min(1, tracks.Count) : tracks.Count;
        var outputs = new List<string> { "[vout]" };
        outputs.AddRange(Enumerable.Range(0, audioOutputs).Select(i => $"[aout{i}]"));
        filter.Add($"{string.Concat(concat)}concat=n={segments.Count}:v=1:a={audioOutputs}{string.Concat(outputs)}");
        args.AddRange(["-filter_complex_threads", Math.Clamp(options.Threads, 1, 2).ToString(), "-filter_complex", string.Join(";", filter)]);
        foreach (var output in outputs) args.AddRange(["-map", output]);
        args.AddRange(options.VideoArguments);
        if (options.Intermediate) args.AddRange(["-c:a", "pcm_s24le", "-ar", "48000", "-ac", "2", destination]);
        else args.AddRange(["-c:a", "aac", "-b:a", "320k", "-ar", "48000", "-ac", "2", "-movflags", "+faststart", destination]);
        return args;
    }
}
