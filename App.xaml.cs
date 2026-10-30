using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using FlyleafLib;

namespace ReelGood;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) => LogStartupFailure(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogStartupFailure(args.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (e.Args.Length == 1 && e.Args[0] == "--verify-package")
        {
            // A non-interactive package check also verifies the renamed native
            // apphost can still locate its managed assembly and WPF resources.
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                if (assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyProductAttribute), false)
                    .OfType<System.Reflection.AssemblyProductAttribute>().Single().Product != "GlitchStich")
                    throw new InvalidDataException("The package has the wrong product identity.");
                foreach (var resource in new[] { "ReelGood.tools.ffmpeg.exe", "ReelGood.Audio.Intro.wav" })
                    using (var embedded = assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException(resource))
                        if (embedded.Length == 0) throw new InvalidDataException(resource);
                foreach (var asset in new[] { "Resources/reelgood.ico", "Resources/Brand/reelgood-app.png", "Resources/Brand/copilot-camera.png" })
                {
                    var decoded = System.Windows.Media.Imaging.BitmapDecoder.Create(new Uri("pack://application:,,,/ReelGood;component/" + asset), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    if (decoded.Frames[0].PixelWidth == 0) throw new InvalidDataException(asset);
                }
                foreach (var library in new[] { "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll" })
                    if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "Flyleaf", "FFmpeg", library))) throw new FileNotFoundException(library);
                Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(2); }
            return;
        }
        SplashWindow? splash = null;
        try
        {
            LogStartupFailure("Startup entered.");
            var flyleafFfmpegPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Flyleaf", "FFmpeg");
            var requiredPlaybackLibraries = new[] { "avcodec-63.dll", "avformat-63.dll", "avutil-61.dll" };
            var missingPlaybackLibrary = requiredPlaybackLibraries.FirstOrDefault(name => !File.Exists(Path.Combine(flyleafFfmpegPath, name)));
            if (missingPlaybackLibrary is not null)
                throw new FileNotFoundException($"The Flyleaf playback runtime is incomplete. Missing {missingPlaybackLibrary}. Rebuild GlitchStich to install its playback files.", Path.Combine(flyleafFfmpegPath, missingPlaybackLibrary));
            Engine.Start(new EngineConfig
            {
                FFmpegPath = flyleafFfmpegPath,
                UIRefresh = false,
                UIRefreshInterval = 250
            });
            // Show the editor immediately so a splash animation can never block startup.
            var editor = new MainWindow();
            LogStartupFailure("Editor window constructed.");
            MainWindow = editor;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Exit += (_, _) => LogStartupFailure("Application exit requested.");
            editor.Show();
            LogStartupFailure("Editor window shown.");

            try
            {
                splash = new SplashWindow
                {
                    Owner = editor,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = editor.Left,
                    Top = editor.Top,
                    Width = editor.ActualWidth,
                    Height = editor.ActualHeight
                };
                splash.Show();
                var intro = splash.PlayIntroAsync();
                await Task.WhenAny(intro, Task.Delay(TimeSpan.FromSeconds(6.5)));
                if (intro.IsFaulted) throw intro.Exception?.GetBaseException() ?? new InvalidOperationException("The intro animation failed.");
            }
            catch (Exception ex)
            {
                LogStartupFailure(ex);
            }
            finally
            {
                // Reveal the full editing workspace once the intro ends (or fails).
                if (editor.IsVisible) editor.WindowState = WindowState.Maximized;
                if (splash?.IsVisible == true) splash.Close();
            }
        }
        catch (Exception ex)
        {
            LogStartupFailure(ex);
            CyberDialog.Show(null, $"GlitchStich could not open the editor.\n\n{ex.Message}", "GlitchStich startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void LogStartupFailure(object exception)
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "ReelGoodData");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} {exception}\n");
        }
        catch { }
    }
}




