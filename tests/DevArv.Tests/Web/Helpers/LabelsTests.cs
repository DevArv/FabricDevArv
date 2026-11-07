using DevArv.Web.Helpers;

namespace DevArv.Tests.Web.Helpers;

public class LabelsTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(7, "00:07")]
    [InlineData(65, "01:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3 * 3600 + 5 * 60 + 9, "3:05:09")]
    [InlineData(26 * 3600, "26:00:00")]
    public void Stopwatch_FormatsElapsedTime(int Seconds, string Expected) =>
        Assert.Equal(Expected, Labels.Stopwatch(TimeSpan.FromSeconds(Seconds)));

    [Fact]
    public void Stopwatch_IgnoresFractionsOfSecond() =>
        Assert.Equal("00:59", Labels.Stopwatch(TimeSpan.FromMilliseconds(59_999)));

    [Fact]
    public void Stopwatch_NegativeIsZero() =>
        Assert.Equal("00:00", Labels.Stopwatch(TimeSpan.FromSeconds(-30)));
}
