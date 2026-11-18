using System.Windows.Media;
using System.Windows.Threading;
using Arcadia.Models;

namespace Arcadia.Services;

public enum Sfx { Click, Tick, Navigate, Open, Back, Launch, FavoriteOn, FavoriteOff, Error, Notify, Toggle, Pop, PopNav, Clack, ClackNav, Tock, TockNav, Startup }

/// <summary>
/// Subtle UI sounds, synthesised at first start (no external assets) and played through WPF MediaPlayers.
/// </summary>
public sealed class SoundService
{
    private const int Rate = 44100;
    /// <summary>Folder of the generated sounds: change it whenever the synthesis changes.</summary>
    public const string SoundsVersion = "v2";
    private readonly AppSettings _settings;
    private readonly Dictionary<Sfx, MediaPlayer[]> _players = new();
    private readonly Dictionary<Sfx, int> _next = new();
    private readonly HashSet<MediaPlayer> _priming = new();
    private readonly HashSet<MediaPlayer> _used = new();
    private DispatcherTimer? _primeTimer;
    private MediaPlayer? _wake;
    private DateTime _lastTick;

    public SoundService(AppSettings settings) => _settings = settings;

    /// <summary>
    /// Creates the players. The very first playback of a MediaPlayer, and of an audio output that was asleep
    /// (Bluetooth headsets, HDMI screens), loses its beginning: the first sound of a session was reduced to a
    /// tiny blip. So every player is primed by a silent playback, and a near-silent stream wakes the output up
    /// for the first seconds (startup sound, "new games detected" notification).
    /// </summary>
    public void Initialize()
    {
        try
        {
            // v2: sounds rebuilt after the fix of the bells (v1 files were mostly silent)
            var dir = Path.Combine(AppPaths.DataDir, "sounds", SoundsVersion);
            Directory.CreateDirectory(dir);
            foreach (var old in Directory.EnumerateDirectories(Path.Combine(AppPaths.DataDir, "sounds")).Where(d => Path.GetFileName(d) != SoundsVersion))
                try { Directory.Delete(old, true); } catch { /* in use */ }
            foreach (Sfx sfx in Enum.GetValues<Sfx>())
            {
                var file = Path.Combine(dir, sfx + ".wav");
                if (!File.Exists(file)) File.WriteAllBytes(file, ToWav(Synthesize(sfx)));
                var count = sfx is Sfx.Tick or Sfx.Click or Sfx.Pop or Sfx.Clack or Sfx.Tock ? 3 : sfx == Sfx.Startup ? 1 : 2;
                var players = new MediaPlayer[count];
                for (var i = 0; i < count; i++)
                {
                    var p = new MediaPlayer();
                    p.MediaOpened += (_, _) => Prime(p);
                    p.Open(new Uri(file));
                    players[i] = p;
                }
                _players[sfx] = players;
                _next[sfx] = 0;
            }
            if (_settings.SoundEnabled) WakeOutput(dir);
        }
        catch (Exception ex) { Log.Warn($"Sound init failed: {ex.Message}"); }
    }

    /// <summary>Silent playback: the player's audio pipeline is ready before its first real sound.</summary>
    private void Prime(MediaPlayer p)
    {
        if (_used.Contains(p)) return; // a real sound was asked before the file finished opening
        try
        {
            p.Volume = 0;
            _priming.Add(p);
            p.Play();
            _primeTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => EndPriming(), Dispatcher.CurrentDispatcher);
            _primeTimer.Stop();
            _primeTimer.Start();
        }
        catch { /* audio device unavailable */ }
    }

    private void EndPriming()
    {
        _primeTimer?.Stop();
        foreach (var p in _priming)
            try { p.Stop(); } catch { /* ignore */ }
        _priming.Clear();
    }

    /// <summary>Plays an inaudible stream for the first seconds so that a sleeping output is awake for real sounds.</summary>
    private void WakeOutput(string dir)
    {
        var file = Path.Combine(dir, "Wake.wav");
        if (!File.Exists(file))
        {
            // -80 dB dither rather than pure zeros: some drivers skip a stream of digital silence
            var rng = new Random(3);
            var samples = new float[(int)(1.0 * Rate)];
            for (var i = 0; i < samples.Length; i++) samples[i] = (float)((rng.NextDouble() * 2 - 1) * 0.0001);
            File.WriteAllBytes(file, ToWav(samples, normalize: false));
        }
        _wake = new MediaPlayer { Volume = 1 };
        var until = DateTime.Now.AddSeconds(15);
        _wake.MediaEnded += (_, _) =>
        {
            if (DateTime.Now > until) { _wake.Close(); _wake = null; return; }
            _wake.Position = TimeSpan.Zero;
            _wake.Play();
        };
        _wake.Open(new Uri(file));
        _wake.Play();
    }

    /// <summary>The startup sound, once the window appears (not when Arcadia starts hidden in the tray).</summary>
    public void PlayStartup()
    {
        // a short delay: the output wakes up, and the sound comes with the window's fade-in
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
        if (!_settings.SoundEnabled || !_players.TryGetValue(sfx, out var pool)) return;
        if (isTick)
        {
            if ((DateTime.Now - _lastTick).TotalMilliseconds < 45) return;
            _lastTick = DateTime.Now;
        }
        try
        {
            var i = _next[sfx];
            _next[sfx] = (i + 1) % pool.Length;
            var p = pool[i];
            _priming.Remove(p);
            _used.Add(p);
            p.Volume = Math.Clamp(_settings.SoundVolume, 0, 1) * Gain(sfx);
            p.Stop();
            p.Position = TimeSpan.Zero;
            p.Play();
        }
        catch { /* audio device unavailable */ }
    }

    private static double Gain(Sfx s) => s switch
    {
        Sfx.Tick or Sfx.Pop or Sfx.Clack or Sfx.Tock => 0.45,
        Sfx.Click => 0.6,
        Sfx.Launch => 0.9,
        Sfx.Startup => 0.8,
        _ => 0.75,
    };

    // ------------------------------------------------------------------------------------
    // Synthesis
    // ------------------------------------------------------------------------------------

    public static float[] Synthesize(Sfx sfx) => sfx switch
    {
        Sfx.Click => Mix(0.09, Bell(1320, 0, 0.05, 0.35), Bell(1980, 0, 0.03, 0.12)),
        Sfx.Tick => Mix(0.05, Bell(1650, 0, 0.025, 0.28)),
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
        // warm rising chord (G - D - G - D - G) over a soft swell, with a little sparkle on top
        Sfx.Startup => Reverb(Mix(1.6,
            Glide(98, 196, 0, 0.9, 0.08),
            Bell(392.0, 0.00, 1.0, 0.18), Bell(587.3, 0.09, 1.0, 0.18), Bell(784.0, 0.18, 1.1, 0.2),
            Bell(1174.7, 0.27, 1.2, 0.17), Bell(1568.0, 0.36, 1.0, 0.1), Bell(3136.0, 0.36, 0.4, 0.025)), 0.5),
        // console-flavoured navigation (original sounds, only the character is evoked)
        Sfx.Pop => Mix(0.09, Glide(900, 1500, 0, 0.05, 0.3), Bell(1500, 0.03, 0.05, 0.1)),
        Sfx.PopNav => Reverb(Mix(0.25, Glide(700, 1250, 0, 0.09, 0.28), Bell(1870, 0.06, 0.12, 0.1)), 0.3),
        Sfx.Clack => Mix(0.04, Noise(0, 0.012, 0.35), Bell(2400, 0, 0.012, 0.18, harmonic: 0.5)),
        Sfx.ClackNav => Mix(0.08, Noise(0, 0.015, 0.4), Bell(1800, 0, 0.03, 0.2, harmonic: 0.4), Bell(2700, 0.025, 0.03, 0.12)),
        Sfx.Tock => Mix(0.07, Bell(880, 0, 0.03, 0.26, harmonic: 0.05), Bell(440, 0, 0.04, 0.1, harmonic: 0)),
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

    private static byte[] ToWav(float[] samples, bool normalize = true)
    {
        // fade out the last 5 ms and normalise softly
        var peak = samples.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var norm = normalize && peak > 0.95f ? 0.95f / peak : 1f;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataLen = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        var fade = (int)(0.005 * Rate);
        for (var i = 0; i < samples.Length; i++)
        {
            var g = i > samples.Length - fade ? (samples.Length - i) / (float)fade : 1f;
            w.Write((short)Math.Clamp(samples[i] * norm * g * 32767, short.MinValue, short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }
}
