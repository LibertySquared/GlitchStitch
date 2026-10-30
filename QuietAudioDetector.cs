using System.IO;
using System.Text;

namespace ReelGood;

internal static class QuietAudioDetector
{
    // Only derived 16 kHz mono PCM snippets (max 12 seconds) are read here.
    // All supplied independent audible tracks must be quiet in the same bins.
    public static IReadOnlyList<EditorialRange> Find(IReadOnlyList<string> paths, double length)
    {
        if (paths.Count == 0 || !double.IsFinite(length) || length <= 0 || length > 12.1) return [];
        const int rate = 16000, binSamples = 1600;
        var count = (int)Math.Floor(length * 10); var quiet = Enumerable.Repeat(true, count).ToArray();
        foreach (var path in paths)
        {
            using var file = File.OpenRead(path); using var reader = new BinaryReader(file, Encoding.ASCII);
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return [];
            reader.ReadUInt32(); if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return [];
            bool format = false, data = false;
            while (file.Position + 8 <= file.Length)
            {
                var id = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32();
                var next = file.Position + size + (size % 2); if (next > file.Length) return [];
                if (id == "fmt " && size >= 16)
                    format = reader.ReadUInt16() == 1 && reader.ReadUInt16() == 1 && reader.ReadUInt32() == rate &&
                        reader.ReadUInt32() == rate * 2 && reader.ReadUInt16() == 2 && reader.ReadUInt16() == 16;
                else if (id == "data")
                {
                    if (!format) return [];
                    for (var bin = 0; bin < count; bin++)
                    {
                        if ((bin + 1L) * binSamples * 2 > size) { quiet[bin] = false; continue; }
                        for (var sample = 0; sample < binSamples; sample++)
                            if (Math.Abs((int)reader.ReadInt16()) > 327) quiet[bin] = false; // peak below -40 dBFS
                    }
                    data = true; break;
                }
                file.Position = next;
            }
            if (!data) return [];
        }
        var result = new List<EditorialRange>(); int start = -1;
        for (var bin = 0; bin <= count; bin++)
        {
            if (bin < count && quiet[bin]) { if (start < 0) start = bin; continue; }
            if (start >= 0)
            {
                // Preserve a second on both sides so a quiet boundary is not a hard speech cut.
                var a = start / 10d + 1; var b = bin / 10d - 1;
                if (b - a >= 2) result.Add(new(a, b, "All reviewed audible tracks below -40 dBFS; one-second edge handles"));
                start = -1;
            }
        }
        return result;
    }
}
