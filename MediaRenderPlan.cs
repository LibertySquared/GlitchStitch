using System.Globalization;
using System.IO;

namespace GlitchStich;

internal sealed record ResolvedMedia(TimedMedia Placement, EditingAsset? Asset, double Start, double Duration);
internal static class MediaRenderPlan
{
    private static string N(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string P(string path) => path.Replace("\\", "/").Replace(":", "\\:").Replace("'", "'\\''");
    public static IReadOnlyList<ResolvedMedia> Resolve(IReadOnlyList<EditingClip> clips, IReadOnlyList<EditingAsset> assets, IReadOnlyList<TimedMedia> media, double sourceDuration)
    {
        var total = clips.Sum(clip => clip.Duration); var result = new List<ResolvedMedia>();
        foreach (var item in media)
        {
            if (item.Validate(sourceDuration, assets) is { } error) throw new ArgumentException(error);
            var start = EditTimeMap.ToOutput(item.SourceAnchor, clips, item.Occurrence); if (start is null) continue;
            var asset = assets.FirstOrDefault(asset => asset.Id == item.AssetId);
            var duration = Math.Min(item.Duration, total - start.Value);
            if (asset?.Kind is "Video" or "Audio" && asset.Duration > 0) duration = Math.Min(duration, asset.Duration - item.AssetOffset);
            if (duration > .05) result.Add(new(item, asset, start.Value, duration));
        }
        return result;
    }

    public static List<string> Build(string source, string destination, IReadOnlyList<ResolvedMedia> media, int width, int height,
        double duration, bool baseHasAudio, IReadOnlyDictionary<string, string> textFiles, ExportOptions options)
    {
        if (width <= 0 || height <= 0 || duration <= 0 || !double.IsFinite(duration)) throw new ArgumentException("Invalid stitched video dimensions or duration.");
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Media rendering must use a different output file.");
        var args = new List<string> { "-y", "-v", "error", "-threads", "2", "-i", source };
        var filters = new List<string> { "[0:v:0]setsar=1[v0]" }; var video = "v0"; var audio = new List<string>(); var input = 1; int layer = 0;
        if (baseHasAudio) { filters.Add("[0:a:0]asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo[basea]"); audio.Add("[basea]"); }
        foreach (var entry in media)
        {
            var item = entry.Placement; var end = entry.Start + entry.Duration; layer++;
            if (item.Kind == "Text")
            {
                if (!textFiles.TryGetValue(item.Id, out var caption)) throw new ArgumentException("Caption file is missing.");
                filters.Add($"[{video}]drawtext=fontfile='{P(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"))}':textfile='{P(caption)}':expansion=none:fontcolor=white:fontsize={Math.Max(16, (int)(height * item.Size * .2))}:box=1:boxcolor=black@0.55:boxborderw=8:x='min(w-tw,w*{N(item.X)})':y='min(h-th,h*{N(item.Y)})':enable='between(t,{N(entry.Start)},{N(end)})'[v{layer}]");
                video = $"v{layer}"; continue;
            }
            var asset = entry.Asset ?? throw new ArgumentException("Media file is missing."); var index = input++;
            if (item.Kind == "Image") args.AddRange(["-loop", "1", "-framerate", "30"]);
            else args.AddRange(["-ss", N(item.AssetOffset)]);
            args.AddRange(["-t", N(entry.Duration), "-threads", "2", "-i", asset.Path]);
            if (item.Kind is "Image" or "Video")
            {
                var w = Math.Max(2, (int)(width * item.Size) / 2 * 2); var h = Math.Max(2, (int)(height * item.Size) / 2 * 2);
                filters.Add($"[{index}:v:0]setpts=PTS-STARTPTS+{N(entry.Start)}/TB,scale={w}:{h}:force_original_aspect_ratio=decrease:force_divisible_by=2,format=rgba[overlay{layer}]");
                filters.Add($"[{video}][overlay{layer}]overlay=x='min(W-w,W*{N(item.X)})':y='min(H-h,H*{N(item.Y)})':eof_action=pass:repeatlast=0:enable='between(t,{N(entry.Start)},{N(end)})'[v{layer}]");
                video = $"v{layer}";
            }
            if ((item.Kind == "Audio" || asset.HasAudio) && item.Volume > 0)
            {
                var streams = asset.AudioStreams.Count > 0 ? asset.AudioStreams.Select(stream => stream.ToString(CultureInfo.InvariantCulture)).ToArray() : ["a:0"];
                for (var stream = 0; stream < streams.Length; stream++)
                {
                    filters.Add($"[{index}:{streams[stream]}]asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo,volume={N(item.Volume)},apad,atrim=duration={N(entry.Duration)},adelay={Math.Round(entry.Start * 1000)}:all=1[mediaa{layer}_{stream}]");
                    audio.Add($"[mediaa{layer}_{stream}]");
                }
            }
        }
        filters.Add($"[{video}]format=yuv420p[vout]");
        if (audio.Count > 0) filters.Add($"{string.Concat(audio)}amix=inputs={audio.Count}:normalize=0:duration=longest,alimiter=limit=0.95:level=0:latency=1,apad,atrim=duration={N(duration)}[aout]");
        args.AddRange(["-filter_complex_threads", "2", "-filter_complex", string.Join(";", filters), "-map", "[vout]"]);
        if (audio.Count > 0) args.AddRange(["-map", "[aout]", "-c:a", "aac", "-b:a", "320k", "-ac", "2", "-ar", "48000"]);
        args.AddRange(options.VideoArguments); args.AddRange(["-t", N(duration), "-movflags", "+faststart", destination]);
        return args;
    }
}
