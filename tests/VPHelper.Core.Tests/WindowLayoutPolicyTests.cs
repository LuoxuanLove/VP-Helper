using VPHelper.WinUI.Services;
using Xunit;

namespace VPHelper.Core.Tests;

public class WindowLayoutPolicyTests
{
    [Fact]
    public void FirstLaunch_IsCenteredAndSixteenByTen()
    {
        var result = WindowLayoutPolicy.Restore(null, new WindowBounds(0, 0, 1920, 1040), 1);
        Assert.Equal(new WindowBounds(240, 70, 1440, 900), result);
    }

    [Theory]
    [InlineData(1920, 1040, 1)]
    [InlineData(2560, 1392, 1.75)]
    [InlineData(1366, 728, 1.5)]
    [InlineData(1080, 1872, 1)]
    [InlineData(768, 600, 2)]
    [InlineData(3440, 1392, 1.25)]
    public void SizingAndRestore_RespectWorkAreaScaleAndAspect(int workWidth, int workHeight, double scale)
    {
        var work = new WindowBounds(-workWidth, 50, workWidth, workHeight);
        var limits = WindowLayoutPolicy.GetLimits(workWidth, workHeight, scale);
        var random = new Random(42);
        for (var i = 0; i < 300; i++)
        {
            var (w, h) = WindowLayoutPolicy.Constrain(random.Next(1, 6000), random.Next(1, 4000), limits, i % 2 == 0);
            Assert.InRange(w, limits.MinWidth, limits.MaxWidth);
            Assert.InRange(h, limits.MinHeight, limits.MaxHeight);
            Assert.InRange((double)w / h, limits.MinAspect - .00001, limits.MaxAspect + .00001);
            var restored = WindowLayoutPolicy.Restore(new WindowBounds(9000, -4000, w, h), work, scale);
            Assert.InRange(restored.X, work.X, work.X + work.Width - restored.Width);
            Assert.InRange(restored.Y, work.Y, work.Y + work.Height - restored.Height);
        }
    }
}
