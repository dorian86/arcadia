using Arcadia.Services;
using Xunit;

namespace Arcadia.Tests;

public class SoundTests
{
    [Fact]
    public void EverySoundIsAudible()
    {
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            var samples = SoundService.Synthesize(sfx);
            var peak = samples.Max(Math.Abs);
            // the bells used to stop at their first sample: 8 sounds of 17 were completely silent
            Assert.True(peak > 0.08, $"{sfx} is (almost) silent: peak {peak:0.000}");
            Assert.True(samples.Take(Math.Min(samples.Length, 4410)).Any(s => Math.Abs(s) > 0.02), $"{sfx} does not start within 0.1 s");
        }
    }

    [Fact]
    public void EverySoundEndsInSilence()
    {
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            var samples = SoundService.FromWav(SoundService.ToWav(SoundService.Synthesize(sfx)));
            Assert.NotEmpty(samples);
            // the last millisecond is silent: no click when the sound stops
            Assert.True(samples.TakeLast(44).All(s => Math.Abs(s) < 0.01f), $"{sfx} is cut while still ringing");
        }
    }

    [Fact]
    public void AReplayedSoundFadesOutInsteadOfBeingCut()
    {
        var tone = Enumerable.Range(0, 4000).Select(i => 0.6f * MathF.Sin(i * 0.2f)).ToArray();
        var voices = new List<AudioMixer.Voice> { new() { Samples = tone, Key = 1 } };
        var buffer = new float[400];
        AudioMixer.MixInto(buffer, voices);
        voices[0].Fade = 176; // what Play() does to the previous copy of a navigation sound
        var all = new List<float>(buffer);
        while (voices.Count > 0) { AudioMixer.MixInto(buffer, voices); all.AddRange(buffer); }
        // never a jump bigger than the tone itself makes from one sample to the next
        var maxStep = all.Zip(all.Skip(1), (a, b) => Math.Abs(b - a)).Max();
        Assert.True(maxStep < 0.13f, $"step {maxStep}");
        Assert.True(all.Count < 400 * 3, "the faded voice is dropped");
    }

    [Fact]
    public void TheLimiterNeverClips()
    {
        Assert.InRange(AudioMixer.ToPcm(0.5f), (short)16382, (short)16384);
        Assert.InRange(AudioMixer.ToPcm(3f), 30000, short.MaxValue);
        Assert.InRange(AudioMixer.ToPcm(-3f), short.MinValue, -30000);
        Assert.True(AudioMixer.ToPcm(0.95f) < AudioMixer.ToPcm(1.2f));
    }

    [Fact]
    public void StartupSoundHasItsChord()
    {
        var s = SoundService.Synthesize(Sfx.Startup);
        Assert.InRange(s.Length / 44100.0, 1.5, 2.5);
        // still ringing after half a second (rising chord), faded out at the end
        Assert.Contains(s.Skip(22050).Take(4410), x => Math.Abs(x) > 0.05);
        Assert.True(s.TakeLast(441).All(x => Math.Abs(x) < 0.05));
    }
}
