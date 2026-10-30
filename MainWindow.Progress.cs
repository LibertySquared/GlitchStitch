using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;

namespace ReelGood;

public partial class MainWindow
{
    private readonly Stopwatch _taskElapsed = new();
    private DispatcherTimer? _taskTicker;
    private string _progressCaption = "READY";

    private void ReportTaskProgress(string stage, double completed, double total)
    {
        TaskProgress.IsIndeterminate = false;
        TaskProgress.Value = total > 0 ? Math.Clamp(completed / total * 100, 0, 100) : 0;
        _progressCaption = total > 0
            ? $"{stage.ToUpperInvariant()}  {TaskProgress.Value:0}%  /  {completed:0.#} OF {total:0.#}"
            : $"{stage.ToUpperInvariant()}  /  WAITING FOR PROGRESS";
        RefreshTaskCaption();
    }

    private void RefreshTaskCaption() => TaskProgressLabel.Text = _busyOperations > 0
        ? $"{_progressCaption}  ·  {_taskElapsed.Elapsed:mm\\:ss} elapsed" : _progressCaption;

    private void BeginTaskProgress()
    {
        _taskElapsed.Restart();
        _taskTicker ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _taskTicker.Tick -= TaskTicker_Tick;
        _taskTicker.Tick += TaskTicker_Tick;
        _taskTicker.Start();
        ReportTaskProgress("Preparing", 0, 0);
    }

    private void TaskTicker_Tick(object? sender, EventArgs args) => RefreshTaskCaption();

    private static bool TryProgressNumber(string value, out double number) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
