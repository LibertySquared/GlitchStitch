using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

internal static class CopilotLiveChecks
{
    public static async Task Run(bool textOnly = false, bool mediaOnly = false)
    {
        const string root = @"D:\Editor\GlitchStich";
        var folder = Path.Combine(root, "bin", "Release", "net9.0-windows", "win-x64");
        if (!File.Exists(Path.Combine(folder, "GlitchStich.dll"))) folder = Path.Combine(root, ".glitchstich-publish-staging");
        if (!File.Exists(Path.Combine(folder, "GlitchStich.dll"))) folder = Path.Combine(root, "portable-release");
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? Assembly.LoadFrom(Path.Combine(folder, name.Name + ".dll")) : null;
        var assembly = Assembly.LoadFrom(Path.Combine(folder, "GlitchStich.dll")); var type = assembly.GetType("GlitchStich.MainWindow", true)!;
        var runner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(runner, value);
        Set("_ffmpegPath", Path.Combine(root, "tools", "ffmpeg.exe")); Set("_copilotEndpoint", "http://localhost:11434");
        Set("_eventPreferences", Activator.CreateInstance(type.GetField("_eventPreferences", flags)!.FieldType)!);
        var output = Path.Combine(root, "checks", "results", "copilot-review", "production"); Directory.CreateDirectory(output);
        var results = new List<object>();
        foreach (var source in textOnly ? Array.Empty<string>() : new[] {
            @"D:\Videos\NVIDIAFortnite\Fortnite 2026.10.11 - 12.40.24.06.Double Elimination.DVR.mp4",
            @"D:\Videos\Medal\Edits\MedalTVGearsofWarEDay20261025205351168-trim-1793289290085.mp4" })
        {
            Set("_videoPath", source);
            var audio = (System.Collections.IList)Activator.CreateInstance(type.GetField("_audioTracks", flags)!.FieldType)!;
            var trackType = assembly.GetType("GlitchStich.MainWindow+AudioTrack", true)!;
            var probe = new ProcessStartInfo(Path.Combine(root, "tools", "ffprobe.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-v", "error", "-select_streams", "a", "-show_streams", "-of", "json", source }) probe.ArgumentList.Add(argument);
            using var process = Process.Start(probe)!;
            using var metadata = JsonDocument.Parse(await process.StandardOutput.ReadToEndAsync()); await process.WaitForExitAsync();
            var streams = metadata.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            var labels = streams.Select((stream, index) => GlitchStich.AudioTrackPolicy.ReadLabel(stream, index + 1)).ToArray();
            for (var i = 0; i < streams.Length; i++)
            {
                if (GlitchStich.AudioTrackPolicy.IsRedundantCombined(labels[i], labels)) continue;
                audio.Add(Activator.CreateInstance(trackType, [streams[i].GetProperty("index").GetInt32(), labels[i], "aac", streams[i].GetProperty("channels").GetInt32()]));
            }
            Set("_audioTracks", audio);
            var name = source.Contains("NVIDIAFortnite") ? "fortnite" : "gears";
            var sampleFolder = Path.Combine(output, name);
            if (Directory.Exists(sampleFolder)) Directory.Delete(sampleFolder, true);
            var start = name == "fortnite" ? 18d : 0d; var watch = Stopwatch.StartNew();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var task = (Task)type.GetMethod("ReviewFootageWindowAsync", flags)!.Invoke(runner, ["eliminations", "Integration test: judge actual media, not this label", start, 12d, Path.Combine(output, name), cancel.Token])!;
            await task; var verdict = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var json = JsonSerializer.Serialize(verdict, verdict.GetType());
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.GetProperty("FramesSupplied").GetInt32() == 0 || parsed.RootElement.GetProperty("AudioTracksSupplied").GetInt32() == 0 || parsed.RootElement.GetProperty("Reason").GetString()!.Length < 12) throw new Exception("Actual review evidence was missing");
            if (mediaOnly && name == "gears" && parsed.RootElement.GetProperty("ContextType").GetString() is not ("CharacterDialogue" or "Cutscene"))
                throw new Exception("The known character-dialogue control was not identified as protected context");
            Console.WriteLine($"LIVE {name}: {json}; {watch.Elapsed.TotalSeconds:0.0}s");
            results.Add(new { source, review = parsed.RootElement.Clone(), seconds = watch.Elapsed.TotalSeconds });
        }
        if (!textOnly) File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        var client = assembly.GetType("GlitchStich.CopilotLocalClient", true)!;
        await (Task)client.GetMethod("UnloadAsync")!.Invoke(null, ["http://localhost:11434", "gemma4:e2b"])!;
        if (mediaOnly) return;
        var schema = assembly.GetType("GlitchStich.CopilotProposal", true)!.GetProperty("Schema")!.GetValue(null)!;
        var messages = new object[] { new { role = "system", content = "You are GlitchStich Copilot. Return the given JSON schema with a specific explanation, prompt refinement, and any relevant bookmark edits. Bookmark decision MUST be null, preserving Keep/Skip. A scan prompt describes what to detect, not instructions for editing bookmark IDs. Explain the actual proposed change directly. Context: source duration=60, id=0,start=10,end=20,eventTime=15,label=Laughter,userDecision=unreviewed. User prefers four seconds of lead-in." }, new { role = "user", content = "Make the prompt focus on actual group laughter, and adjust bookmark 0 to include four seconds before the event." } };
        messages[0] = new { role = "system", content = "You are GlitchStich Copilot. Return the given JSON schema with a specific explanation, prompt refinement, and relevant bookmark edits. For N seconds before an event, copy the known eventTime and set leadInSeconds=N; the app calculates start. Bookmark decision MUST be null, preserving Keep/Skip. A scan prompt describes what to detect, not instructions for editing bookmark IDs. Describe proposals, never claim they are applied. Context: source duration=60, id=0,start=10,end=20,eventTime=15,label=Laughter,userDecision=unreviewed. User prefers four seconds of lead-in." };
        var textModel = Environment.GetEnvironmentVariable("GLITCHSTICH_TEST_COPILOT_MODEL") ?? "gemma4:e2b";
        var reply = await (Task<string>)client.GetMethod("ChatAsync")!.Invoke(null, ["http://localhost:11434", textModel, messages, schema, CancellationToken.None])!;
        var parsedReply = GlitchStich.CopilotProposal.Parse(reply);
        if (parsedReply.Proposals.Count == 0 || parsedReply.Message.StartsWith("Review these", StringComparison.OrdinalIgnoreCase)) throw new Exception("Live chat did not return concrete suggestions");
        File.WriteAllText(Path.Combine(output, "text-reply.json"), reply);
        if (!parsedReply.Proposals.Any(proposal => proposal.BookmarkId == 0 && proposal.Start == 11 && proposal.End == 20)) throw new Exception("Live Copilot failed the explicit four-second context request");
        Console.WriteLine("LIVE text: " + reply);
        await (Task)client.GetMethod("UnloadAsync")!.Invoke(null, ["http://localhost:11434", textModel])!;
    }
}
