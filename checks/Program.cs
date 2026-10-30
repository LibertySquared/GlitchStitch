using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReelGood;

if (args.Length == 1 && args[0] == "--render") { RenderChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--playback-close") { PlaybackCloseChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--editing") { EditingChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--youtube") { YouTubeChecks.Run().GetAwaiter().GetResult(); return; }
if (args.Length == 1 && args[0] == "--window-close") { WindowCloseChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--preview-sync") { WindowCloseChecks.Run(sync: true); return; }
if (args.Length == 1 && args[0] == "--media-live") { WindowCloseChecks.Run(true); return; }
DraftPlanChecks.Run();
EditorialChecks.Run();
CopilotChecks.Run();
if (args.Length == 1 && args[0] == "--copilot-live") { CopilotLiveChecks.Run().GetAwaiter().GetResult(); return; }
if (args.Length == 1 && args[0] == "--copilot-text-live") { CopilotLiveChecks.Run(true).GetAwaiter().GetResult(); return; }
if (args.Length == 1 && args[0] == "--editorial-live") { CopilotLiveChecks.Run(false, true).GetAwaiter().GetResult(); return; }
AudioPlaybackTrack[] playback = [new(true, false, null), new(false, false, "PC"), new(false, false, "MIC"), new(false, true, "MUSIC"), new(false, false, "PC")];
if (!AudioTrackPolicy.Conflicts(1, playback).SequenceEqual(new[] { 0, 4 }) || !AudioTrackPolicy.Conflicts(0, playback).SequenceEqual(new[] { 1, 2, 4 }) || AudioTrackPolicy.Conflicts(3, playback).Any()) throw new Exception("Duplicate playback policy muted distinct audio or allowed doubled playback");
Console.WriteLine("PASS Playback uses either the combined mix or its components, and only one copy of identical audio");
if (args.Length == 1 && args[0] == "--export-quality") { ExportHardwareChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--draft-footage")
{
    DraftFootageChecks.Run();
    DraftFootageChecks.Run(@"D:\Videos\Medal\Edits\MedalTVGearsofWarEDay20261025205351168-trim-1793289290085.mp4");
    return;
}
var proposals = CopilotProposal.Parse("""{"message":"Review context","prompt":"laughter with reactions","bookmarks":[{"id":0,"start":2,"end":8,"decision":"Keep"}]}""");
if (proposals.Proposals.Count != 2 || proposals.Proposals.Any(p => p.Validate(10, 1) is not null)) throw new Exception("Valid Copilot changes rejected");
if (CopilotProposal.Parse("""{"message":"Context","prompt":null,"bookmarks":[null,{"id":"bad","start":0,"end":5},{"id":0,"start":2,"end":8,"eventTime":null,"decision":null}]}""").Proposals.Count != 1) throw new Exception("Malformed or null proposal fields were not handled safely");
foreach (var invalid in new[] {
    new CopilotProposal(null, 1, 2, 8, null), new(null, 0, -1, 8, null), new(null, 0, 8, 2, null),
    new(null, 0, 2, 11, null), new(null, 0, double.NaN, 8, null), new(null, 0, 2, double.PositiveInfinity, null),
    new(null, 0, 2, 8, "Delete"), new(null, 0, 2, 8, null, double.NaN), new(null, 0, 2, 8, null, 9), new(" ", null, null, null, null), new("test", 0, 2, 8, null) })
    if (invalid.Validate(10, 1) is null) throw new Exception("Unsafe Copilot change accepted");
Console.WriteLine("PASS Copilot proposal parsing and rejection of stale IDs, invalid ranges, non-finite times, and unsupported actions");

if (args.Length == 2 && (args[0] == "--footage" || args[0] == "--new-footage"))
{
    FootageChecks.Run(args[1], args[0] == "--new-footage");
    return;
}

var root = Path.Combine(AppContext.BaseDirectory, "fixtures");
using (var metadata = JsonDocument.Parse("[{\"tags\":{\"name\":\"All Audio\"}},{\"tags\":{\"name\":\"All PC Audio\"}},{\"tags\":{\"name\":\"Microphone\"}}]"))
{
    var names = metadata.RootElement.EnumerateArray().Select((stream, i) => AudioTrackPolicy.ReadLabel(stream, i + 1)).ToArray();
    if (!names.SequenceEqual(new[] { "All Audio", "All PC Audio", "Microphone" })) throw new Exception("Capture track names were lost.");
    if (!AudioTrackPolicy.IsRedundantCombined(names[0], names) || AudioTrackPolicy.IsRedundantCombined(names[1], names)) throw new Exception("Combined-track classification failed.");
    if (AudioTrackPolicy.IsRedundantCombined("All Audio", ["All Audio"])) throw new Exception("A sole mixed track must stay audible.");
    Console.WriteLine("PASS Medal names and redundant-mix policy; sole mixed audio stays enabled");
}
Directory.CreateDirectory(root);
var ffmpeg = @"D:\Editor\ReelGood\tools\ffmpeg.exe";
var ffprobe = @"D:\Editor\ReelGood\tools\ffprobe.exe";
string Run(string tool, IEnumerable<string> args)
{
    var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
    foreach (var arg in args) info.ArgumentList.Add(arg);
    using var process = Process.Start(info)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit(); Task.WaitAll(stdout, stderr);
    if (process.ExitCode != 0) throw new Exception(stderr.Result);
    return stdout.Result + stderr.Result;
}
void Check(bool okay, string message) { if (!okay) throw new Exception(message); Console.WriteLine("PASS " + message); }
var save = Path.Combine(root, "atomic.reelgood");
ProjectFileStore.Write(save, "{\"version\":1}");
ProjectFileStore.Write(save, "{\"version\":2}");
Check(JsonDocument.Parse(File.ReadAllText(save)).RootElement.GetProperty("version").GetInt32() == 2, "Saved project replaced with complete JSON");
Check(JsonDocument.Parse(File.ReadAllText(save + ".bak")).RootElement.GetProperty("version").GetInt32() == 1, "Previous saved project retained as backup");
Check(!Directory.GetFiles(root, "*.tmp").Any(), "Save leaves no temporary files");
var source = Path.Combine(root, "source.mp4"); var wav = Path.Combine(root, "external.wav");
Run(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=15:d=4", "-f", "lavfi", "-i", "sine=frequency=440:duration=4", "-f", "lavfi", "-i", "sine=frequency=880:duration=4", "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "libx264", "-c:a", "aac", source]);
Run(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=220:duration=4", wav]);
var duplicate = Path.Combine(root, "duplicate.wav");
Run(ffmpeg, ["-y", "-v", "error", "-i", wav, "-c:a", "copy", "-metadata", "comment=different header, same PCM", duplicate]);
var different = Path.Combine(root, "different.wav");
Run(ffmpeg, ["-y", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=330:duration=4", different]);
Check(AudioDuplicateDetector.Fingerprint(wav) == AudioDuplicateDetector.Fingerprint(duplicate), "Identical PCM is recognized despite different WAV metadata");
Check(AudioDuplicateDetector.Fingerprint(wav) != AudioDuplicateDetector.Fingerprint(different), "Distinct microphone/game audio is not classified as an exact duplicate");
Check(ExportOptions.WorkerBudget(4, 8L << 30, false) <= 2 && ExportOptions.WorkerBudget(32, 64L << 30, false) <= 6 && ExportOptions.WorkerBudget(16, 32L << 30, true) <= 2, "Low/mid/high CPU budgets stay bounded and low-impact mode reserves workers");
var mixed = Path.Combine(root, "mixed.mp4");
Run(ffmpeg, ExportPlan.Build(source, mixed, [new(0, 1), new(2, 3)], [new(1, null, .5, false), new(-1, wav, .5, false)]));
using (var mixedInfo = JsonDocument.Parse(Run(ffprobe, ["-v", "error", "-show_streams", "-of", "json", mixed])))
    Check(mixedInfo.RootElement.GetProperty("streams").EnumerateArray().Count(item => item.GetProperty("codec_type").GetString() == "audio") == 1, "Two audible source/WAV tracks export as one stereo audio stream");
var pcm = Path.Combine(root, "mixed.raw");
Run(ffmpeg, ["-y", "-v", "error", "-ss", "0.2", "-i", mixed, "-t", "0.5", "-vn", "-ac", "1", "-ar", "48000", "-f", "f32le", pcm]);
var bytes = File.ReadAllBytes(pcm); var samples = Enumerable.Range(0, bytes.Length / 4).Select(index => (double)BitConverter.ToSingle(bytes, index * 4)).ToArray();
double Tone(double hz) { double re = 0, im = 0; for (var i = 0; i < samples.Length; i++) { var phase = 2 * Math.PI * hz * i / 48000; re += samples[i] * Math.Cos(phase); im += samples[i] * Math.Sin(phase); } return Math.Sqrt(re * re + im * im) * 2 / samples.Length; }
Check(Tone(220) > .02 && Tone(440) > .02 && samples.Max(Math.Abs) < .98, "Both independent audio signals survive the mix without clipping");
var balanced = Path.Combine(root, "balanced.mp4");
Run(ffmpeg, ExportPlan.Build(source, balanced, [new(0, 1)], [], new ExportOptions("Balanced")));
using (var balancedInfo = JsonDocument.Parse(Run(ffprobe, ["-v", "error", "-show_streams", "-of", "json", balanced])))
    Check(balancedInfo.RootElement.GetProperty("streams")[0].GetProperty("r_frame_rate").GetString() == "15/1", "Balanced export does not invent higher source frame rates");
void CheckExport(string name, ExportSegment[] segments, ExportAudio[] audio, int expectedAudio, double duration)
{
    var output = Path.Combine(root, name + ".mp4");
    Run(ffmpeg, ExportPlan.Build(source, output, segments, audio, new ExportOptions(MergeAudio: false)));
    using var probe = JsonDocument.Parse(Run(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", output]));
    Check(probe.RootElement.GetProperty("streams").EnumerateArray().Count(s => s.GetProperty("codec_type").GetString() == "audio") == expectedAudio, name + " has expected audio tracks");
    var length = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    Check(Math.Abs(length - duration) < .2, name + " preserves edited duration");
    if (expectedAudio > 0)
    {
        var level = Run(ffmpeg, ["-hide_banner", "-i", output, "-map", "0:a:0", "-af", "volumedetect", "-f", "null", "-"]);
        var value = double.Parse(Regex.Match(level, @"mean_volume: ([-\d.]+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Check(value < -30 && value > -37, name + " applies quarter-volume gain");
        var silence = Run(ffmpeg, ["-hide_banner", "-i", output, "-map", "0:a:1", "-af", "volumedetect", "-f", "null", "-"]);
        Check(silence.Contains("mean_volume: -91"), name + " external muted track is silent");
    }
}
var tracks = new[] { new ExportAudio(2, null, .25, false), new ExportAudio(-1, wav, .8, true) };
CheckExport("clip", [new(0, 1)], tracks, 2, 1);
CheckExport("timeline", [new(0, 1), new(2, 3)], tracks, 2, 2);
CheckExport("video-only", [new(0, 1)], [], 0, 1);
var recording = Path.Combine(root, "recording.mp4");
Run(ffmpeg, RecordingMux.Build(Path.Combine(root, "video-only.mp4"), [(wav, "System audio"), (wav, "Microphone")], recording));
using (var probe = JsonDocument.Parse(Run(ffprobe, ["-v", "error", "-show_streams", "-show_format", "-of", "json", recording])))
{
    Check(probe.RootElement.GetProperty("streams").EnumerateArray().Count(s => s.GetProperty("codec_type").GetString() == "audio") == 2, "Recording finalization retains two independent audio streams");
    var seconds = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    Check(seconds < 1.2, "Recording finalization stops at the video duration");
}
try { ExportPlan.Build(source, source, [new(0, 1)], []); throw new Exception("Source overwrite was allowed"); }
catch (ArgumentException) { Console.WriteLine("PASS Source video overwrite rejected"); }
Console.WriteLine("ALL CHECKS PASSED");
var appFolder = @"D:\Editor\ReelGood\bin\Release\net9.0-windows\win-x64";
if (!File.Exists(Path.Combine(appFolder, "ReelGood.dll"))) appFolder = @"D:\Editor\ReelGood\.reelgood-publish-staging";
if (!File.Exists(Path.Combine(appFolder, "ReelGood.dll"))) appFolder = @"D:\Editor\ReelGood\portable-release";
System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(appFolder, name.Name + ".dll");
    return File.Exists(path) ? System.Reflection.Assembly.LoadFrom(path) : null;
};
var app = System.Reflection.Assembly.LoadFrom(Path.Combine(appFolder, "ReelGood.dll"));
var mainType = app.GetType("ReelGood.MainWindow", true)!;
var runner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(mainType);
mainType.GetField("_ffmpegPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(runner, ffmpeg);
mainType.GetField("_exportCpuBudget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(runner, 1);
using (var cancelExport = new CancellationTokenSource(400))
{
    var task = (Task)mainType.GetMethod("RunExportProcessAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(runner,
        [new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "color=s=160x90:r=15:d=30", "-f", "null", "-" }, cancelExport.Token, (Action<double>)(_ => { })])!;
    try { task.GetAwaiter().GetResult(); throw new Exception("Export cancellation did not interrupt the child process"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS Export cancellation stops the active FFmpeg child process"); }
}
var documentType = app.GetType("ReelGood.MainWindow+ProjectDocument", true)!;
var oldProject = "{\"Version\":1,\"VideoSegments\":[],\"AudioTracks\":[{\"StreamIndex\":1,\"Label\":\"Mic\",\"Codec\":\"aac\",\"Channels\":1}]}";
var project = JsonSerializer.Deserialize(oldProject, documentType)!;
Check(((System.Collections.ICollection)documentType.GetProperty("VideoSegments")!.GetValue(project)!).Count == 0, "Empty timeline survives project deserialization");
Check(documentType.GetProperty("DraftBackup")!.GetValue(project) is null, "Older projects open without a draft restore snapshot");
Check(documentType.GetProperty("MediaLibraryPaths")!.GetValue(project) is null, "Older projects do not inherit the global media library");
Check(documentType.GetProperty("SecondaryEditingMode")!.GetValue(project) is null, "Older project saves keep the second editing mode disabled");
var effectsProject = JsonSerializer.Deserialize("""{"Version":1,"VideoSegments":[{"StartSeconds":2,"EndSeconds":8,"TrackIndex":1,"Effects":{"Speed":2,"Rotation":90,"Volume":0.5}}],"EditingAssets":[{"Id":"image","Path":"D:\\Videos\\overlay.png","Kind":"Image","Description":"Celebration","AllowAi":true,"AudioStreams":[]}],"TimedMedia":[{"Id":"overlay","AssetId":"image","Kind":"Image","SourceAnchor":4,"Duration":2,"Occurrence":1}]}""", documentType)!;
effectsProject = JsonSerializer.Deserialize(JsonSerializer.Serialize(effectsProject, documentType), documentType)!;
var savedClips = (System.Collections.IList)documentType.GetProperty("VideoSegments")!.GetValue(effectsProject)!;
var savedEffects = savedClips[0]!.GetType().GetProperty("Effects")!.GetValue(savedClips[0])!;
var savedMedia = (System.Collections.IList)documentType.GetProperty("TimedMedia")!.GetValue(effectsProject)!;
Check((double)savedEffects.GetType().GetProperty("Speed")!.GetValue(savedEffects)! == 2 && (int)savedEffects.GetType().GetProperty("Rotation")!.GetValue(savedEffects)! == 90 && (int)savedMedia[0]!.GetType().GetProperty("Occurrence")!.GetValue(savedMedia[0])! == 1 && ((System.Collections.ICollection)documentType.GetProperty("EditingAssets")!.GetValue(effectsProject)!).Count == 1, "Clip effects, imported editing assets, timed overlays and repeated-clip anchors survive project save/open round trip");
var editorialProject = JsonSerializer.Deserialize("""{"Version":1,"GameTitle":"Gears of War","GameMode":"Campaign","EditingMode":0,"ProtectedSceneRanges":[{"Start":20,"End":40,"Reason":"CharacterDialogue"}],"QuietDowntimeRanges":[{"Start":50,"End":55,"Reason":"Reviewed quiet waiting"}]}""", documentType)!;
editorialProject = JsonSerializer.Deserialize(JsonSerializer.Serialize(editorialProject, documentType), documentType)!;
documentType.GetProperty("SecondaryEditingMode")!.SetValue(editorialProject, 2);
editorialProject = JsonSerializer.Deserialize(JsonSerializer.Serialize(editorialProject, documentType), documentType)!;
Check((int)documentType.GetProperty("SecondaryEditingMode")!.GetValue(editorialProject)! == 2, "Optional second editing mode survives project save/open round trip");
Check((string)documentType.GetProperty("GameMode")!.GetValue(editorialProject)! == "Campaign" &&
    ((System.Collections.ICollection)documentType.GetProperty("ProtectedSceneRanges")!.GetValue(editorialProject)!).Count == 1 &&
    ((System.Collections.ICollection)documentType.GetProperty("QuietDowntimeRanges")!.GetValue(editorialProject)!).Count == 1,
    "Game mode, scene protection and proposed quiet cuts survive project save/open round trip");
documentType.GetProperty("MediaLibraryPaths")!.SetValue(project, new List<string> { @"D:\Videos\session.mp4", @"D:\Videos\voice.wav", @"D:\Videos\overlay.png" });
var libraryProject = JsonSerializer.Deserialize(JsonSerializer.Serialize(project, documentType), documentType)!;
Check(((List<string>)documentType.GetProperty("MediaLibraryPaths")!.GetValue(libraryProject)!).SequenceEqual(new[] { @"D:\Videos\session.mp4", @"D:\Videos\voice.wav", @"D:\Videos\overlay.png" }), "Project-specific video, audio and PNG library survives save/open round trip");
using (var sound = app.GetManifestResourceStream("ReelGood.Audio.DraftReady.wav") ?? throw new Exception("Draft-ready sound is not embedded"))
{
    using var reader = new BinaryReader(sound);
    Check(new string(reader.ReadChars(4)) == "RIFF" && sound.Length > 1000, "Draft-ready notification WAV is embedded in the app");
}
var draftSaved = oldProject[..^1] + ",\"DraftBackup\":{\"LayerCount\":3,\"Segments\":[{\"StartSeconds\":2,\"EndSeconds\":8,\"TrackIndex\":2}],\"ClipStart\":2,\"ClipEnd\":8}}";
var withBackup = JsonSerializer.Deserialize(draftSaved, documentType)!;
withBackup = JsonSerializer.Deserialize(JsonSerializer.Serialize(withBackup, documentType), documentType)!;
var backup = documentType.GetProperty("DraftBackup")!.GetValue(withBackup)!;
var restoredSegments = (System.Collections.IList)backup.GetType().GetProperty("Segments")!.GetValue(backup)!;
Check((int)backup.GetType().GetProperty("LayerCount")!.GetValue(backup)! == 3 && restoredSegments.Count == 1 && (int)restoredSegments[0]!.GetType().GetProperty("TrackIndex")!.GetValue(restoredSegments[0])! == 2, "Pre-draft video ranges and layers survive project save/open round trip");
var savedAudio = ((System.Collections.IList)documentType.GetProperty("AudioTracks")!.GetValue(project)!)[0]!;
Check((double)savedAudio.GetType().GetProperty("Volume")!.GetValue(savedAudio)! == .8, "Older project files receive default audio gain");
var editedProject = oldProject.Replace("\"Channels\":1", "\"Channels\":1,\"Volume\":0.25,\"IsMuted\":true,\"DuplicateOf\":\"PC audio\",\"DuplicateFingerprint\":\"COPYHASH\"");
project = JsonSerializer.Deserialize(editedProject, documentType)!;
project = JsonSerializer.Deserialize(JsonSerializer.Serialize(project, documentType), documentType)!;
savedAudio = ((System.Collections.IList)documentType.GetProperty("AudioTracks")!.GetValue(project)!)[0]!;
Check((double)savedAudio.GetType().GetProperty("Volume")!.GetValue(savedAudio)! == .25 && (bool)savedAudio.GetType().GetProperty("IsMuted")!.GetValue(savedAudio)!, "Audio mute and gain survive project save/open round trip");
Check((string?)savedAudio.GetType().GetProperty("DuplicateFingerprint")!.GetValue(savedAudio) == "COPYHASH", "Duplicate audio identity survives project save/open round trip");
var naudio = System.Reflection.Assembly.LoadFrom(Path.Combine(appFolder, "NAudio.Wasapi.dll"));
foreach (var name in new[] { "NAudio.CoreAudioApi.WasapiCapture", "NAudio.Wave.WasapiLoopbackCapture" })
{
    using var capture = (IDisposable)Activator.CreateInstance(naudio.GetType(name, true)!)!;
    Check(true, name + " initializes on this PC without recording");
}
Console.WriteLine("PROJECT AND AUDIO INITIALIZATION CHECKS PASSED");
