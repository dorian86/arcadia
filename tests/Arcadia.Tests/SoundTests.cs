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
    public void StartupSoundHasItsChord()
    {
        var s = SoundService.Synthesize(Sfx.Startup);
        Assert.InRange(s.Length / 44100.0, 1.5, 2.5);
        // still ringing after half a second (rising chord), faded out at the end
        Assert.Contains(s.Skip(22050).Take(4410), x => Math.Abs(x) > 0.05);
        Assert.True(s.TakeLast(441).All(x => Math.Abs(x) < 0.05));
    }
}
