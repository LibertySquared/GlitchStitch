using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GlitchStich;

internal static class AudioDuplicateDetector
{
    // Compare complete PCM payloads and format, not WAV headers or loudness peaks.
    public static string Fingerprint(string path, Action<long, long>? progress = null, CancellationToken cancellation = default)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Expected a PCM WAV file.");
        reader.ReadUInt32(); if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Expected WAV audio.");
        byte[]? format = null; long data = 0, size = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var name = new string(reader.ReadChars(4)); var length = reader.ReadUInt32(); var next = stream.Position + length + (length & 1);
            if (next > stream.Length + 1) throw new InvalidDataException("Incomplete WAV audio.");
            if (name == "fmt ") { if (length < 16 || length > 4096) throw new InvalidDataException("Invalid WAV format."); format = reader.ReadBytes(16); }
            if (name == "data") { data = stream.Position; size = length; }
            stream.Position = Math.Min(next, stream.Length);
        }
        if (format is null || data == 0 || size == 0) throw new InvalidDataException("No PCM audio found.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(format);
        stream.Position = data; var buffer = new byte[128 * 1024]; long done = 0, reported = 0;
        while (done < size)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, size - done));
            if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); done += read;
            if (done - reported >= 8 * 1024 * 1024 || done == size) { progress?.Invoke(done, size); reported = done; }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
