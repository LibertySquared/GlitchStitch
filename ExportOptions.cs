namespace ReelGood;

internal sealed record ExportOptions(string Quality = "High", string Encoder = "libx264", int Threads = 2, bool MergeAudio = true, bool HardwareDecode = false, bool Intermediate = false)
{
    public int CanvasWidth { get; init; }
    public int CanvasHeight { get; init; }
    public int QualityValue => Quality switch { "Balanced" => 21, "Maximum" => 16, _ => 18 };
    public static int WorkerBudget(int cores, long memoryBytes, bool lowImpact) => lowImpact || cores <= 4 || memoryBytes < 12L * 1024 * 1024 * 1024 ? Math.Clamp(cores / 2, 1, 2) : Math.Clamp(cores / 2, 2, 6);
    public IEnumerable<string> VideoArguments => Encoder switch
    {
        "h264_nvenc" => ["-c:v", Encoder, "-preset", Quality == "Maximum" ? "p5" : "p4", "-rc", "vbr", "-cq", QualityValue.ToString(), "-b:v", "0"],
        "h264_qsv" => ["-c:v", Encoder, "-preset", "fast", "-global_quality", QualityValue.ToString(), "-look_ahead", "0", "-async_depth", "2"],
        "h264_amf" => ["-c:v", Encoder, "-quality", "balanced", "-rc", "cqp", "-qp_i", QualityValue.ToString(), "-qp_p", QualityValue.ToString()],
        _ => ["-c:v", "libx264", "-preset", Threads <= 2 ? "veryfast" : "fast", "-crf", QualityValue.ToString(), "-threads", Math.Clamp(Threads, 1, 6).ToString()]
    };
}
