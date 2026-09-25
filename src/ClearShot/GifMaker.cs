using ClearShot.Capture;

namespace ClearShot;

/// <summary>Turns a recording into a looping GIF.</summary>
internal static class GifMaker
{
    /// <summary>Saves the recording as a GIF with ClearShot's own encoder (see <see cref="GifWriter"/>).</summary>
    public static void Save(Recording recording, string path) => GifWriter.Save(recording, path);

    /// <summary>
    /// GIF delays are in hundredths of a second. Rounding each frame separately drifts, so round the running
    /// total instead; also respect the 2-hundredths minimum that browsers enforce.
    /// </summary>
    internal static int[] CentisecondDelays(int[] durationsMs)
    {
        var delays = new int[durationsMs.Length];
        long elapsedMs = 0;
        int elapsedCs = 0;
        for (int i = 0; i < durationsMs.Length; i++)
        {
            elapsedMs += durationsMs[i];
            int targetCs = (int)Math.Round(elapsedMs / 10.0);
            delays[i] = Math.Max(2, targetCs - elapsedCs);
            elapsedCs += delays[i];
        }
        return delays;
    }
}
