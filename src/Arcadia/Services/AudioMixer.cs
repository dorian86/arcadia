using System.Runtime.InteropServices;

namespace Arcadia.Services;

/// <summary>
/// A tiny software mixer on one continuous output stream (winmm waveOut, 44.1 kHz, 16-bit mono).
/// Replaces WPF MediaPlayers, which cut a sound dead when it was replayed (audible clicks), stuttered under
/// load and lost the beginning of their first playback. Here every sound is mixed sample by sample:
/// a replayed sound fades out in 4 ms, overlapping sounds add up through a soft limiter, and the stream
/// closes after a few seconds of silence (so it never keeps the audio device busy).
/// </summary>
public sealed class AudioMixer : IDisposable
{
    public const int Rate = 44100;
    private const int BufferFrames = 882;        // 20 ms
    private const int BufferCount = 3;           // ~60 ms of latency
    private const int FadeFrames = Rate * 4 / 1000;
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(6);

    /// <summary>A sound being played.</summary>
    public sealed class Voice
    {
        public required float[] Samples { get; init; }
        public required int Key { get; init; }
        public float Gain { get; init; } = 1;
        public int Position;
        /// <summary>Frames left before silence when stopping (-1 = playing normally).</summary>
        public int Fade = -1;
        public bool Done => Position >= Samples.Length || Fade == 0;
    }

    private readonly object _lock = new();
    private readonly List<Voice> _voices = new();
    private Thread? _thread;
    private volatile bool _running;
    private DateTime _lastSound;
    private bool _failed;
    private static bool _logged;

    /// <summary>
    /// Plays a sound. <paramref name="maxInstances"/>: how many copies of the same sound may overlap
    /// (the oldest fades out beyond that; 1 for navigation ticks).
    /// </summary>
    public void Play(float[] samples, int key, float gain, int maxInstances)
    {
        if (_failed || samples.Length == 0) return;
        lock (_lock)
        {
            var same = _voices.Where(v => v.Key == key && v.Fade < 0).ToList();
            for (var i = 0; i <= same.Count - maxInstances; i++) same[i].Fade = FadeFrames;
            _voices.Add(new Voice { Samples = samples, Key = key, Gain = gain });
            _lastSound = DateTime.UtcNow;
            if (_running) return;
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "Arcadia audio", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
    }

    // ------------------------------------------------------------------------------------
    // Mixing (pure, tested)
    // ------------------------------------------------------------------------------------

    /// <summary>Adds the voices into <paramref name="output"/> and drops the finished ones.</summary>
    public static void MixInto(float[] output, List<Voice> voices)
    {
        Array.Clear(output);
        foreach (var v in voices)
        {
            for (var i = 0; i < output.Length && !v.Done; i++)
            {
                var s = v.Samples[v.Position++] * v.Gain;
                if (v.Fade > 0) s *= v.Fade-- / (float)FadeFrames;
                output[i] += s;
            }
        }
        voices.RemoveAll(v => v.Done);
    }

    /// <summary>Soft limiter: transparent up to 0.8, then rounds the peaks off instead of clipping them.</summary>
    public static short ToPcm(float x)
    {
        var a = Math.Abs(x);
        if (a > 0.8f) a = 0.8f + 0.2f * MathF.Tanh((a - 0.8f) / 0.2f);
        return (short)(Math.Sign(x) * a * 32767);
    }

    // ------------------------------------------------------------------------------------
    // Output (winmm)
    // ------------------------------------------------------------------------------------

    private void Run()
    {
        IntPtr device = IntPtr.Zero;
        var headers = new IntPtr[BufferCount];
        var data = new IntPtr[BufferCount];
        using var ready = new AutoResetEvent(false);
        try
        {
            var format = new WaveFormat { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = Rate * 2 };
            if (waveOutOpen(out device, WaveMapper, ref format, ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent) != 0)
            {
                Log.Warn("Audio output unavailable (waveOutOpen failed)");
                _failed = true;
                return;
            }
            if (!_logged) { _logged = true; Log.Info("Audio output opened (waveOut, 44.1 kHz)"); }
            var mix = new float[BufferFrames];
            var pcm = new short[BufferFrames];
            for (var b = 0; b < BufferCount; b++)
            {
                data[b] = Marshal.AllocHGlobal(BufferFrames * 2);
                headers[b] = Marshal.AllocHGlobal(HeaderSize);
                for (var i = 0; i < HeaderSize; i += 4) Marshal.WriteInt32(headers[b], i, 0);
                Marshal.WriteIntPtr(headers[b], 0, data[b]);
                Marshal.WriteInt32(headers[b], 8, BufferFrames * 2);
                waveOutPrepareHeader(device, headers[b], HeaderSize);
                Fill(headers[b], data[b], mix, pcm);
                waveOutWrite(device, headers[b], HeaderSize);
            }

            while (true)
            {
                ready.WaitOne(50);
                lock (_lock)
                {
                    // nothing to play for a while: close the stream (reopened by the next sound)
                    if (_voices.Count == 0 && DateTime.UtcNow - _lastSound > IdleClose)
                    {
                        _running = false;
                        break;
                    }
                }
                for (var b = 0; b < BufferCount; b++)
                {
                    if ((Marshal.ReadInt32(headers[b], FlagsOffset) & WhdrDone) == 0) continue;
                    Fill(headers[b], data[b], mix, pcm);
                    waveOutWrite(device, headers[b], HeaderSize);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Audio output stopped: {ex.Message}");
            lock (_lock) _running = false;
        }
        finally
        {
            if (device != IntPtr.Zero)
            {
                waveOutReset(device);
                foreach (var h in headers.Where(h => h != IntPtr.Zero)) waveOutUnprepareHeader(device, h, HeaderSize);
                waveOutClose(device);
            }
            foreach (var p in headers.Concat(data).Where(p => p != IntPtr.Zero)) Marshal.FreeHGlobal(p);
        }
    }

    private void Fill(IntPtr header, IntPtr buffer, float[] mix, short[] pcm)
    {
        lock (_lock) MixInto(mix, _voices);
        for (var i = 0; i < mix.Length; i++) pcm[i] = ToPcm(mix[i]);
        Marshal.Copy(pcm, 0, buffer, pcm.Length);
        // clear WHDR_DONE before handing the buffer back (keep WHDR_PREPARED)
        Marshal.WriteInt32(header, FlagsOffset, Marshal.ReadInt32(header, FlagsOffset) & ~WhdrDone);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _voices.Clear();
            _lastSound = DateTime.MinValue;
        }
    }

    // ---- winmm ----
    private const uint WaveMapper = unchecked((uint)-1);
    private const uint CallbackEvent = 0x00050000;
    private const int WhdrDone = 1;
    private static readonly int HeaderSize = IntPtr.Size == 8 ? 48 : 32;
    private static readonly int FlagsOffset = IntPtr.Size == 8 ? 24 : 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort wFormatTag, nChannels;
        public int nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);
}
