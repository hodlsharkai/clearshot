using Windows.Media.Control;

namespace ClearShot;

/// <summary>
/// Pauses whatever is playing (YouTube, Netflix, Spotify and anything else that shows in Windows' media controls)
/// and later resumes exactly those, leaving anything you had already paused alone. Games aren't media sessions,
/// so they're never touched.
/// </summary>
internal static class MediaPauser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(600);
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;

    /// <summary>Connects to Windows' media controls ahead of time so the first pause is quick.</summary>
    public static async Task WarmUpAsync()
    {
        try { await ManagerAsync(); }
        catch (Exception ex) { Log.Write($"Media controls unavailable: {ex.Message}"); }
    }

    /// <returns>The sessions that were playing and are now paused. Never throws.</returns>
    public static async Task<IReadOnlyList<GlobalSystemMediaTransportControlsSession>> PausePlayingAsync()
    {
        var paused = new List<GlobalSystemMediaTransportControlsSession>();
        try
        {
            var work = PauseAllAsync(paused);
            if (await Task.WhenAny(work, Task.Delay(Timeout)) != work) Log.Write("Pausing media timed out");
        }
        catch (Exception ex)
        {
            Log.Write($"Could not pause media: {ex.Message}");
        }
        lock (paused) return paused.ToArray();
    }

    public static async Task ResumeAsync(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        foreach (var session in sessions)
        {
            try { await session.TryPlayAsync(); }
            catch (Exception ex) { Log.Write($"Could not resume {session.SourceAppUserModelId}: {ex.Message}"); }
        }
    }

    /// <summary>Names of the apps currently playing, for diagnostics.</summary>
    public static async Task<IReadOnlyList<string>> PlayingAppsAsync()
    {
        var manager = await ManagerAsync();
        return manager.GetSessions().Where(IsPlaying).Select(s => s.SourceAppUserModelId).ToArray();
    }

    private static async Task PauseAllAsync(List<GlobalSystemMediaTransportControlsSession> paused)
    {
        var manager = await ManagerAsync();
        var pauses = manager.GetSessions().Where(IsPlaying).Select(async session =>
        {
            if (session.GetPlaybackInfo().Controls.IsPauseEnabled && await session.TryPauseAsync())
                lock (paused) paused.Add(session);
        });
        await Task.WhenAll(pauses);
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private static async Task<GlobalSystemMediaTransportControlsSessionManager> ManagerAsync() =>
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
