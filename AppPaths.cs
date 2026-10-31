using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GlitchStich;

internal static class AppPaths
{
    // These names are retained only to import earlier installations and saves.
    private const string LegacyDataName = "ReelGoodData";
    public static string EnsureDataDirectory(string applicationDirectory)
    {
        var target = Path.Combine(Path.GetFullPath(applicationDirectory), "GlitchStichData");
        var legacy = Path.Combine(Path.GetFullPath(applicationDirectory), LegacyDataName);
        if (Directory.Exists(legacy) && (!Directory.Exists(target) || !Directory.EnumerateFileSystemEntries(target).Any()))
        {
            if (Directory.Exists(target)) Directory.Delete(target); // Empty destination only.
            Directory.Move(legacy, target);
        }
        Directory.CreateDirectory(target);
        return target;
    }

    public static string RemapLegacyPath(string value, string dataDirectory)
    {
        if (!Path.IsPathFullyQualified(value) || File.Exists(value) || Directory.Exists(value)) return value;
        foreach (var name in new[] { LegacyDataName, "GlitchStichData" })
        {
            var marker = Path.DirectorySeparatorChar + name + Path.DirectorySeparatorChar;
            var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var candidate = Path.GetFullPath(Path.Combine(dataDirectory, value[(index + marker.Length)..]));
            var root = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (File.Exists(candidate) || Directory.Exists(candidate))) return candidate;
        }
        // The development installation was renamed together with the product.
        var oldRoot = @"D:\Editor\ReelGood\";
        if (value.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(@"D:\Editor\GlitchStich", value[oldRoot.Length..]);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        return value;
    }

    public static string RemapProjectJson(string json, string dataDirectory)
    {
        var document = JsonNode.Parse(json);
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj.ToArray())
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = RemapLegacyPath(text, dataDirectory);
                    else Visit(pair.Value);
            else if (node is JsonArray array)
                for (var index = 0; index < array.Count; index++)
                    if (array[index] is JsonValue value && value.TryGetValue<string>(out var text)) array[index] = RemapLegacyPath(text, dataDirectory);
                    else Visit(array[index]);
        }
        Visit(document);
        return document?.ToJsonString() ?? json;
    }
}
