using SS14.Admin.Helpers;

namespace SS14.Admin.Tests;

public sealed class BanDurationAdjusterTests
{
    [Fact]
    public void AdjustMinutes_AddsToCurrentValue()
    {
        var adjusted = BanDurationAdjuster.AdjustMinutes(1440, 1440);

        Assert.Equal(2880, adjusted);
    }

    [Fact]
    public void AdjustMinutes_SubtractsFromCurrentValue()
    {
        var adjusted = BanDurationAdjuster.AdjustMinutes(2880, -1440);

        Assert.Equal(1440, adjusted);
    }

    [Fact]
    public void AdjustMinutes_ClampsNegativeResultsToPermanent()
    {
        var adjusted = BanDurationAdjuster.AdjustMinutes(60, -1440);

        Assert.Equal(0, adjusted);
    }

    [Fact]
    public void AdjustMinutes_CanApplyRepeatedRelativeChanges()
    {
        var adjusted = 1440;
        adjusted = BanDurationAdjuster.AdjustMinutes(adjusted, 1440);
        adjusted = BanDurationAdjuster.AdjustMinutes(adjusted, 1440);
        adjusted = BanDurationAdjuster.AdjustMinutes(adjusted, -1440);

        Assert.Equal(2880, adjusted);
    }

    [Fact]
    public void FormatMinutes_FormatsCompositeDuration()
    {
        var formatted = BanDurationAdjuster.FormatMinutes(1501);

        Assert.Equal("1 day, 1 hour, 1 minute", formatted);
    }

    [Fact]
    public void FormatMinutes_FormatsPermanent()
    {
        var formatted = BanDurationAdjuster.FormatMinutes(0);

        Assert.Equal("Permanent", formatted);
    }
}
