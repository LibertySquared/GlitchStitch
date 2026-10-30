namespace ReelGood;

internal static class PlaybackShutdown
{
    // Detach WPF surfaces on their dispatcher, then let native decoder threads
    // finish while that dispatcher remains free to handle their final callbacks.
    public static async Task ReleaseAsync(Action detachUi, Action disposeNative)
    {
        try { detachUi(); }
        finally { await Task.Run(disposeNative); }
    }
}
