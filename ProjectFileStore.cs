using System.IO;
using System.Text;

namespace GlitchStich;

internal static class ProjectFileStore
{
    public const string Extension = ".glitch";
    public static string DefaultProjectName(string source) => Path.GetFileNameWithoutExtension(source) + "_glitch" + Extension;
    public static string DefaultExportName(string source) => Path.GetFileNameWithoutExtension(source) + "_stitch.mp4";
    public static void Write(string path, string json)
    {
        path = Path.GetFullPath(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
