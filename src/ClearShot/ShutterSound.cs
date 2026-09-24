using System.Media;

namespace ClearShot;

/// <summary>A short, quiet camera click, synthesised so there's no sound file to ship.</summary>
internal sealed class ShutterSound : IDisposable
{
    private readonly MemoryStream _wav = Build();
    private readonly SoundPlayer _player;

    public ShutterSound()
    {
        _player = new SoundPlayer(_wav);
        _player.Load();
    }

    public void Play()
    {
        try { _player.Play(); }
        catch (Exception ex) { Log.Write($"Sound failed: {ex.Message}"); }
    }

    private static MemoryStream Build()
    {
        const int rate = 44100;
        var samples = new short[(int)(rate * 0.12)];
        var rng = new Random(7);
        // Two soft noise bursts, like a shutter opening and closing.
        AddBurst(samples, rng, rate, start: 0.000, length: 0.035, volume: 0.22);
        AddBurst(samples, rng, rate, start: 0.055, length: 0.045, volume: 0.16);

        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            int dataBytes = samples.Length * 2;
            w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(dataBytes);
            foreach (var s in samples) w.Write(s);
        }
        ms.Position = 0;
        return ms;
    }

    private static void AddBurst(short[] samples, Random rng, int rate, double start, double length, double volume)
    {
        int from = (int)(start * rate), count = (int)(length * rate);
        double previous = 0;
        for (int i = 0; i < count && from + i < samples.Length; i++)
        {
            double envelope = Math.Exp(-i / (count / 5.0));
            // A little low-pass filtering takes the harsh hiss off the noise.
            double noise = rng.NextDouble() * 2 - 1;
            previous = previous * 0.55 + noise * 0.45;
            samples[from + i] = (short)Math.Clamp(samples[from + i] + previous * envelope * volume * short.MaxValue, short.MinValue, short.MaxValue);
        }
    }

    public void Dispose()
    {
        _player.Dispose();
        _wav.Dispose();
    }
}
