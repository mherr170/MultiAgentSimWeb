using MultiAgentSimWeb.Models;

namespace MultiAgentSimWebTests;

// ── MoodLabel ────────────────────────────────────────────────────────────────

public class MoodLabelTests
{
    [Theory]
    [InlineData( 51f, "Hopeful")]
    [InlineData( 50.1f, "Hopeful")]
    [InlineData( 50f, "Upbeat")]     // boundary: 50 is NOT > 50
    [InlineData( 21f, "Upbeat")]
    [InlineData( 20f, "Neutral")]    // boundary: 20 is NOT > 20
    [InlineData( -9f, "Neutral")]
    [InlineData(-10f, "Uneasy")]     // boundary: -10 is NOT > -10
    [InlineData(-29f, "Uneasy")]
    [InlineData(-30f, "Discouraged")]
    [InlineData(-59f, "Discouraged")]
    [InlineData(-60f, "Despairing")]
    [InlineData(-100f, "Despairing")]
    public void MoodLabel_CorrectAtBoundaries(float mood, string expected)
    {
        var m = new AgentMood();
        m.AdjustMood(mood);
        Assert.Equal(expected, m.MoodLabel);
    }
}

// ── StressLabel ───────────────────────────────────────────────────────────────

public class StressLabelTests
{
    // Stress starts at 15f by default.

    [Theory]
    [InlineData(  0f, "Calm")]
    [InlineData( 14f, "Calm")]         // 14 < 15
    [InlineData( 15f, "Mild tension")] // 15 is NOT < 15
    [InlineData( 34f, "Mild tension")]
    [InlineData( 35f, "Stressed")]
    [InlineData( 54f, "Stressed")]
    [InlineData( 55f, "Highly stressed")]
    [InlineData( 74f, "Highly stressed")]
    [InlineData( 75f, "PANICKED")]
    [InlineData(100f, "PANICKED")]
    public void StressLabel_CorrectAtBoundaries(float targetStress, string expected)
    {
        var m = new AgentMood();
        // Stress starts at 15; adjust from there
        m.AdjustStress(targetStress - 15f);
        Assert.Equal(expected, m.StressLabel);
    }
}

// ── TraumaLabel / HopeLabel ───────────────────────────────────────────────────

public class TraumaHopeLabelTests
{
    [Theory]
    [InlineData( 0f,  "Untroubled")]
    [InlineData( 9f,  "Untroubled")]
    [InlineData(10f,  "Shaken")]
    [InlineData(29f,  "Shaken")]
    [InlineData(30f,  "Haunted")]
    [InlineData(54f,  "Haunted")]
    [InlineData(55f,  "Traumatised")]
    [InlineData(74f,  "Traumatised")]
    [InlineData(75f,  "Breaking")]
    [InlineData(100f, "Breaking")]
    public void TraumaLabel_CorrectAtBoundaries(float trauma, string expected)
    {
        var m = new AgentMood();
        m.AdjustTrauma(trauma);
        Assert.Equal(expected, m.TraumaLabel);
    }

    [Theory]
    [InlineData( 0f, "Hopeless")]
    [InlineData( 9f, "Hopeless")]
    [InlineData(10f, "Uncertain")]
    [InlineData(24f, "Uncertain")]
    [InlineData(25f, "Holding on")]
    [InlineData(49f, "Holding on")]
    [InlineData(50f, "Determined")]
    [InlineData(74f, "Determined")]
    [InlineData(75f, "Resolute")]
    [InlineData(100f, "Resolute")]
    public void HopeLabel_CorrectAtBoundaries(float hope, string expected)
    {
        var m = new AgentMood();
        m.AdjustHope(hope);
        Assert.Equal(expected, m.HopeLabel);
    }
}

// ── TrustLabel ────────────────────────────────────────────────────────────────

public class TrustLabelTests
{
    [Theory]
    [InlineData( 86f, "deeply bonded")]
    [InlineData( 85f, "close friend")]   // 85 is NOT > 85
    [InlineData( 71f, "close friend")]
    [InlineData( 70f, "trusted friend")]
    [InlineData( 51f, "trusted friend")]
    [InlineData( 50f, "friendly")]
    [InlineData( 21f, "friendly")]
    [InlineData( 20f, "neutral")]
    [InlineData( -9f, "neutral")]
    [InlineData(-10f, "wary")]
    [InlineData(-29f, "wary")]
    [InlineData(-30f, "suspicious")]
    [InlineData(-59f, "suspicious")]
    [InlineData(-60f, "hostile")]
    [InlineData(-100f, "hostile")]
    public void TrustLabel_CorrectAtBoundaries(float trust, string expected)
    {
        Assert.Equal(expected, AgentMood.TrustLabel(trust));
    }
}

// ── Decay — mood / stress / trauma / hope ────────────────────────────────────

public class MoodDecayTests
{
    [Fact]
    public void Decay_PositiveMood_DecreasesBy3()
    {
        var m = new AgentMood();
        m.AdjustMood(10f);
        m.Decay();
        Assert.Equal(7f, m.Mood, precision: 4);
    }

    [Fact]
    public void Decay_NegativeMood_IncreasesBy3()
    {
        var m = new AgentMood();
        m.AdjustMood(-10f);
        m.Decay();
        Assert.Equal(-7f, m.Mood, precision: 4);
    }

    [Fact]
    public void Decay_MoodDoesNotCrossZero_FromPositive()
    {
        var m = new AgentMood();
        m.AdjustMood(2f);
        m.Decay(); // would be -1 without floor
        Assert.Equal(0f, m.Mood, precision: 4);
    }

    [Fact]
    public void Decay_MoodDoesNotCrossZero_FromNegative()
    {
        var m = new AgentMood();
        m.AdjustMood(-2f);
        m.Decay();
        Assert.Equal(0f, m.Mood, precision: 4);
    }

    [Fact]
    public void Decay_ZeroMood_StaysAtZero()
    {
        var m = new AgentMood();
        // Mood starts at 0
        m.Decay();
        Assert.Equal(0f, m.Mood, precision: 4);
    }

    [Fact]
    public void Decay_ReducesStressBy2()
    {
        var m = new AgentMood();
        m.AdjustStress(20f); // stress = 15 + 20 = 35
        m.Decay();           // 35 - 2 = 33
        Assert.Equal(33f, m.Stress, precision: 4);
    }

    [Fact]
    public void Decay_StressFloorsAtZero()
    {
        var m = new AgentMood();
        m.AdjustStress(-14f); // stress = 15 - 14 = 1
        m.Decay();            // 1 - 2 would be -1, but floored at 0
        Assert.Equal(0f, m.Stress, precision: 4);
    }

    [Fact]
    public void Decay_TraumaHealsBy03()
    {
        var m = new AgentMood();
        m.AdjustTrauma(5f);
        m.Decay();
        Assert.Equal(4.7f, m.Trauma, precision: 4);
    }

    [Fact]
    public void Decay_TraumaFloorsAtZero()
    {
        var m = new AgentMood();
        m.AdjustTrauma(0.2f);
        m.Decay(); // 0.2 - 0.3 would be negative, floored at 0
        Assert.Equal(0f, m.Trauma, precision: 4);
    }

    [Fact]
    public void Decay_HopeFadesBy05()
    {
        var m = new AgentMood();
        m.AdjustHope(5f);
        m.Decay();
        Assert.Equal(4.5f, m.Hope, precision: 4);
    }

    [Fact]
    public void Decay_HopeFloorsAtZero()
    {
        var m = new AgentMood();
        m.AdjustHope(0.3f);
        m.Decay(); // 0.3 - 0.5 would be negative, floored at 0
        Assert.Equal(0f, m.Hope, precision: 4);
    }

    [Fact]
    public void Decay_ZeroTraumaAndHope_StayAtZero()
    {
        var m = new AgentMood();
        // Both start at 0
        m.Decay();
        Assert.Equal(0f, m.Trauma, precision: 4);
        Assert.Equal(0f, m.Hope,   precision: 4);
    }
}

// ── Clamp behaviour ───────────────────────────────────────────────────────────

public class MoodClampTests
{
    [Fact]
    public void AdjustMood_CannotExceed100()
    {
        var m = new AgentMood();
        m.AdjustMood(200f);
        Assert.Equal(100f, m.Mood, precision: 4);
    }

    [Fact]
    public void AdjustMood_CannotGoBelowMinus100()
    {
        var m = new AgentMood();
        m.AdjustMood(-200f);
        Assert.Equal(-100f, m.Mood, precision: 4);
    }

    [Fact]
    public void AdjustStress_CannotExceed100()
    {
        var m = new AgentMood();
        m.AdjustStress(200f);
        Assert.Equal(100f, m.Stress, precision: 4);
    }

    [Fact]
    public void AdjustStress_CannotGoBelowZero()
    {
        var m = new AgentMood();
        m.AdjustStress(-200f);
        Assert.Equal(0f, m.Stress, precision: 4);
    }

    [Fact]
    public void AdjustTrauma_CannotExceed100()
    {
        var m = new AgentMood();
        m.AdjustTrauma(200f);
        Assert.Equal(100f, m.Trauma, precision: 4);
    }

    [Fact]
    public void AdjustTrauma_CannotGoBelowZero()
    {
        var m = new AgentMood();
        m.AdjustTrauma(-200f);
        Assert.Equal(0f, m.Trauma, precision: 4);
    }

    [Fact]
    public void AdjustHope_CannotExceed100()
    {
        var m = new AgentMood();
        m.AdjustHope(200f);
        Assert.Equal(100f, m.Hope, precision: 4);
    }

    [Fact]
    public void AdjustHope_CannotGoBelowZero()
    {
        var m = new AgentMood();
        m.AdjustHope(-200f);
        Assert.Equal(0f, m.Hope, precision: 4);
    }

    [Fact]
    public void AdjustTrust_CannotExceed100()
    {
        var m = new AgentMood();
        m.AdjustTrust("Bob", 200f);
        Assert.Equal(100f, m.GetTrust("Bob"), precision: 4);
    }

    [Fact]
    public void AdjustTrust_CannotGoBelowMinus100()
    {
        var m = new AgentMood();
        m.AdjustTrust("Bob", -200f);
        Assert.Equal(-100f, m.GetTrust("Bob"), precision: 4);
    }
}
