using System.Windows.Media;
using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

public enum Sfx { Click, Tick, Navigate, Open, Back, Launch, FavoriteOn, FavoriteOff, Error, Notify, Toggle, Pop, PopNav, Clack, ClackNav, Tock, TockNav, Startup, Trophy, BootPs, BootSwitch, BootWii }

/// <summary>
/// Subtle UI sounds, synthesised at first start (no external assets, cached as .wav) and played through
/// Arcadia's own mixer (<see cref="AudioMixer"/>): no clicks when a sound is replayed, nothing lost at startup.
/// </summary>
public sealed class SoundService
{
    private const int Rate = AudioMixer.Rate;
    /// <summary>Folder of the generated sounds: change it whenever the synthesis changes.</summary>
    public const string SoundsVersion = "v3";
    private readonly AppSettings _settings;
    private readonly Dictionary<Sfx, float[]> _sounds = new();
    private readonly AudioMixer _mixer = new();
    private DateTime _lastTick;

    public SoundService(AppSettings settings) => _settings = settings;

    /// <summary>Loads the sounds (generated once into the data folder, then read back from there).</summary>
    public void Initialize()
    {
        try
        {
            // v2: bells fixed (v1 files were mostly silent); v3: longer short sounds and smooth endings (no clicks)
            var dir = Path.Combine(AppPaths.DataDir, "sounds", SoundsVersion);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateDirectories(Path.Combine(AppPaths.DataDir, "sounds")).Where(d => Path.GetFileName(d) != SoundsVersion))
                try { Directory.Delete(old, true); } catch { /* in use */ }
            foreach (Sfx sfx in Enum.GetValues<Sfx>())
            {
                var file = Path.Combine(dir, sfx + ".wav");
                byte[] wav;
                if (File.Exists(file)) wav = File.ReadAllBytes(file);
                else File.WriteAllBytes(file, wav = ToWav(Synthesize(sfx)));
                _sounds[sfx] = FromWav(wav);
            }
            try { File.Delete(Path.Combine(dir, "Wake.wav")); } catch { /* left by 1.0.3 */ }
        }
        catch (Exception ex) { Log.Warn($"Sound init failed: {ex.Message}"); }
    }

    /// <summary>Samples of a 16-bit mono .wav written by <see cref="ToWav"/> (anything else: silence).</summary>
    public static float[] FromWav(byte[] wav)
    {
        if (wav.Length < 44 || wav[0] != 'R' || wav[8] != 'W') return Array.Empty<float>();
        var samples = new float[(wav.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(wav, 44 + i * 2) / 32768f;
        return samples;
    }

    /// <summary>The startup sound, once the window appears (not when Arcadia starts hidden in the tray).</summary>
    public void PlayStartup()
    {
        // a short delay: the sound comes with the window's fade-in, after the busy first frames
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) => { timer.Stop(); Play(Sfx.Startup); };
        timer.Start();
    }

    /// <summary>Console theme key: navigation sounds take the flavour of that theme (bubbly, clicky, soft tock).</summary>
    public string Flavor { get; set; } = "default";

    private Sfx Remap(Sfx sfx) => (Flavor, sfx) switch
    {
        ("wii" or "wiiu", Sfx.Tick) => Sfx.Pop,
        ("wii" or "wiiu", Sfx.Navigate) => Sfx.PopNav,
        ("switch" or "switch-light", Sfx.Tick) => Sfx.Clack,
        ("switch" or "switch-light", Sfx.Navigate) => Sfx.ClackNav,
        ("ps3" or "ps4" or "ps5", Sfx.Tick) => Sfx.Tock,
        ("ps3" or "ps4" or "ps5", Sfx.Navigate) => Sfx.TockNav,
        _ => sfx,
    };

    public void Play(Sfx sfx)
    {
        var isTick = sfx == Sfx.Tick;
        sfx = Remap(sfx);
        if (!_settings.SoundEnabled || !_sounds.TryGetValue(sfx, out var samples)) return;
        if (isTick)
        {
            if ((DateTime.Now - _lastTick).TotalMilliseconds < 45) return;
            _lastTick = DateTime.Now;
        }
        try
        {
            var volume = (float)(Math.Clamp(_settings.SoundVolume, 0, 1) * Gain(sfx));
            // navigation sounds replace each other (soft fade); the others may overlap once
            var maxInstances = sfx is Sfx.Tick or Sfx.Pop or Sfx.Clack or Sfx.Tock or Sfx.Navigate or Sfx.PopNav or Sfx.ClackNav or Sfx.TockNav ? 1 : 2;
            _mixer.Play(samples, (int)sfx, volume, maxInstances);
        }
        catch (Exception ex) { Log.Warn($"Sound failed: {ex.Message}"); }
    }

    private static double Gain(Sfx s) => s switch
    {
        Sfx.Tick or Sfx.Pop or Sfx.Clack or Sfx.Tock => 0.45,
        Sfx.Click => 0.6,
        Sfx.Launch => 0.9,
        Sfx.Startup => 0.8,
        Sfx.Trophy => 0.7,
        Sfx.BootPs or Sfx.BootWii => 0.8,
        Sfx.BootSwitch => 0.7,
        _ => 0.75,
    };

    // ------------------------------------------------------------------------------------
    // Synthesis
    // ------------------------------------------------------------------------------------

    public static float[] Synthesize(Sfx sfx) => sfx switch
    {
        // short sounds last long enough for their note to die away (a note cut while ringing clicks)
        Sfx.Click => Mix(0.14, Bell(1320, 0, 0.05, 0.35), Bell(1980, 0, 0.03, 0.12)),
        Sfx.Tick => Mix(0.08, Bell(1650, 0, 0.025, 0.28)),
        Sfx.Toggle => Mix(0.12, Glide(660, 990, 0, 0.07, 0.3)),
        Sfx.Navigate => Reverb(Mix(0.35, Glide(520, 780, 0, 0.12, 0.22), Bell(1040, 0.05, 0.18, 0.12), Noise(0, 0.12, 0.05)), 0.25),
        Sfx.Open => Reverb(Mix(0.6, Bell(659.3, 0, 0.35, 0.32), Bell(987.8, 0.07, 0.45, 0.3), Bell(1975.5, 0.07, 0.2, 0.05)), 0.3),
        Sfx.Back => Reverb(Mix(0.5, Bell(987.8, 0, 0.25, 0.22), Bell(659.3, 0.06, 0.35, 0.24)), 0.25),
        Sfx.Launch => Reverb(Mix(1.4,
            Bell(523.3, 0.00, 0.6, 0.26), Bell(659.3, 0.08, 0.6, 0.26), Bell(784.0, 0.16, 0.7, 0.26),
            Bell(1046.5, 0.24, 0.9, 0.3), Bell(2093.0, 0.24, 0.5, 0.06), Glide(260, 520, 0, 0.45, 0.12)), 0.45),
        Sfx.FavoriteOn => Reverb(Mix(0.4, Glide(740, 1110, 0, 0.09, 0.3), Bell(1480, 0.07, 0.25, 0.16)), 0.2),
        Sfx.FavoriteOff => Mix(0.3, Glide(900, 600, 0, 0.12, 0.25)),
        Sfx.Error => Reverb(Mix(0.55, Bell(440, 0, 0.25, 0.3, harmonic: 0.35), Bell(349.2, 0.12, 0.35, 0.3, harmonic: 0.35)), 0.2),
        Sfx.Notify => Reverb(Mix(0.6, Bell(784, 0, 0.45, 0.26), Bell(1568, 0, 0.3, 0.06)), 0.3),
        // startup screens of the console themes (original sounds, only the mood is evoked)
        Sfx.BootPs => Reverb(Mix(2.4,
            Glide(55, 110, 0, 1.6, 0.14), Bell(220, 0.35, 1.6, 0.12, harmonic: 0.4), Bell(330, 0.5, 1.6, 0.11, harmonic: 0.3),
            Bell(440, 0.65, 1.8, 0.11), Bell(1760, 0.85, 1.2, 0.035)), 0.6),
        Sfx.BootSwitch => Mix(0.45, Noise(0, 0.012, 0.5), Bell(1900, 0, 0.03, 0.26, harmonic: 0.5),
            Noise(0.12, 0.012, 0.6), Bell(2300, 0.12, 0.04, 0.3, harmonic: 0.5)),
        Sfx.BootWii => Reverb(Mix(1.3, Bell(784.0, 0, 0.6, 0.2), Bell(1046.5, 0.12, 0.7, 0.2), Bell(1318.5, 0.24, 0.9, 0.18), Bell(2637.0, 0.24, 0.4, 0.03)), 0.45),
        // bright rising sparkle (C - E - G - C) with a shimmering top
        Sfx.Trophy => Reverb(Mix(1.1,
            Bell(1046.5, 0.00, 0.45, 0.2), Bell(1318.5, 0.07, 0.45, 0.2), Bell(1568.0, 0.14, 0.55, 0.22),
            Bell(2093.0, 0.21, 0.8, 0.22), Bell(3136.0, 0.21, 0.35, 0.05), Glide(523, 1046, 0, 0.2, 0.08)), 0.4),
        // warm rising chord (G - D - G - D - G) over a soft swell, with a little sparkle on top
        Sfx.Startup => Reverb(Mix(1.6,
            Glide(98, 196, 0, 0.9, 0.08),
            Bell(392.0, 0.00, 1.0, 0.18), Bell(587.3, 0.09, 1.0, 0.18), Bell(784.0, 0.18, 1.1, 0.2),
            Bell(1174.7, 0.27, 1.2, 0.17), Bell(1568.0, 0.36, 1.0, 0.1), Bell(3136.0, 0.36, 0.4, 0.025)), 0.5),
        // console-flavoured navigation (original sounds, only the character is evoked)
        Sfx.Pop => Mix(0.16, Glide(900, 1500, 0, 0.05, 0.3), Bell(1500, 0.03, 0.05, 0.1)),
        Sfx.PopNav => Reverb(Mix(0.25, Glide(700, 1250, 0, 0.09, 0.28), Bell(1870, 0.06, 0.12, 0.1)), 0.3),
        Sfx.Clack => Mix(0.06, Noise(0, 0.012, 0.35), Bell(2400, 0, 0.012, 0.18, harmonic: 0.5)),
        Sfx.ClackNav => Mix(0.12, Noise(0, 0.015, 0.4), Bell(1800, 0, 0.03, 0.2, harmonic: 0.4), Bell(2700, 0.025, 0.03, 0.12)),
        Sfx.Tock => Mix(0.12, Bell(880, 0, 0.03, 0.26, harmonic: 0.05), Bell(440, 0, 0.04, 0.1, harmonic: 0)),
        Sfx.TockNav => Reverb(Mix(0.3, Bell(587.3, 0, 0.18, 0.2, harmonic: 0.05), Bell(880, 0.04, 0.2, 0.12, harmonic: 0.05)), 0.35),
        _ => Mix(0.05),
    };

    private delegate void Voice(float[] buffer);

    private static float[] Mix(double seconds, params Voice[] voices)
    {
        var buf = new float[(int)(seconds * Rate)];
        foreach (var v in voices) v(buf);
        return buf;
    }

    /// <summary>Soft bell / mallet: sine with a little harmonic, quick attack, exponential decay.</summary>
    private static Voice Bell(double freq, double start, double decay, double amp, double harmonic = 0.18) => buf =>
    {
        var s0 = (int)(start * Rate);
        for (var i = s0; i < buf.Length; i++)
        {
            var t = (i - s0) / (double)Rate;
            var env = Math.Min(1, t / 0.004) * Math.Exp(-t / Math.Max(0.01, decay / 3));
            // stop once the note has faded: not during the attack, where the envelope starts at 0
            // (that test used to end every bell at its very first sample: most sounds were silent)
            if (t > 0.004 && env < 0.0005) break;
            var x = Math.Sin(2 * Math.PI * freq * t) + harmonic * Math.Sin(2 * Math.PI * freq * 2.01 * t);
            buf[i] += (float)(x * env * amp);
        }
    };

    private static Voice Glide(double f0, double f1, double start, double length, double amp) => buf =>
    {
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        double phase = 0;
        for (var i = 0; i < n && s0 + i < buf.Length; i++)
        {
            var k = i / (double)n;
            var f = f0 + (f1 - f0) * (1 - Math.Pow(1 - k, 2));
            phase += 2 * Math.PI * f / Rate;
            var env = Math.Sin(Math.PI * Math.Min(1, k * 1.15));
            buf[s0 + i] += (float)(Math.Sin(phase) * env * amp);
        }
    };

    private static Voice Noise(double start, double length, double amp) => buf =>
    {
        var rng = new Random(7);
        var s0 = (int)(start * Rate);
        var n = (int)(length * Rate);
        double lp = 0;
        for (var i = 0; i < n && s0 + i < buf.Length; i++)
        {
            var k = i / (double)n;
            lp += (rng.NextDouble() * 2 - 1 - lp) * 0.08; // gentle low-pass
            buf[s0 + i] += (float)(lp * Math.Sin(Math.PI * k) * amp * 4);
        }
    };

    /// <summary>Tiny Schroeder-style reverb for a sense of space.</summary>
    private static float[] Reverb(float[] dry, double mix)
    {
        var tail = (int)(0.35 * Rate);
        var outBuf = new float[dry.Length + tail];
        Array.Copy(dry, outBuf, dry.Length);
        int[] delays = { 1557, 1617, 1491, 1422 };
        foreach (var d in delays)
        {
            var comb = new float[outBuf.Length];
            for (var i = 0; i < outBuf.Length; i++)
            {
                var input = i < dry.Length ? dry[i] : 0f;
                comb[i] = input + (i >= d ? comb[i - d] * 0.72f : 0f);
            }
            for (var i = 0; i < outBuf.Length; i++) outBuf[i] += (float)(comb[i] * mix / delays.Length);
        }
        return outBuf;
    }

    public static byte[] ToWav(float[] samples, bool normalize = true)
    {
        // smooth ending (a quarter of the sound, up to 30 ms, cosine-shaped) and soft normalisation
        samples = FadeOut(samples);
        var peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var norm = normalize && peak > 0.95f ? 0.95f / peak : 1f;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var s in samples)
            w.Write((short)Math.Clamp(s * norm * 32767, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>Cosine fade over the end of the sound: whatever still rings dies away instead of being cut (no click).</summary>
    public static float[] FadeOut(float[] samples)
    {
        var copy = (float[])samples.Clone();
        var fade = Math.Min((int)(0.03 * Rate), copy.Length / 4);
        for (var k = 0; k < fade; k++)
        {
            var i = copy.Length - fade + k;
            copy[i] *= (float)(0.5 + 0.5 * Math.Cos(Math.PI * (k + 1) / fade));
        }
        return copy;
    }
}
