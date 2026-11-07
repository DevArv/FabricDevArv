using DevArv.Web.Helpers;
using DevArv.Web.Models;
using Microsoft.Extensions.Options;

namespace DevArv.Tests.Web.Helpers;

public class ClockTests
{
    private static Clock Create(string TimeZone) => new(Options.Create(new AppOptions { TimeZone = TimeZone }));

    [Fact]
    public void Format_UsesTheConfiguredTimeZone()
    {
        var clock = Create("UTC");

        var text = clock.Format(new DateTimeOffset(2026, 3, 5, 14, 30, 0, TimeSpan.Zero));

        Assert.Equal("05/03/2026 14:30", text);
    }

    [Fact]
    public void Format_ConvertsFromUtcToTheZoneOffset()
    {
        var clock = Create("America/Santo_Domingo"); // UTC-4 sin horario de verano

        var text = clock.Format(new DateTimeOffset(2026, 3, 5, 14, 30, 0, TimeSpan.Zero));

        Assert.Equal("05/03/2026 10:30", text);
    }

    [Fact]
    public void Format_NullDate_ReturnsEmptyText() =>
        Assert.Equal("", Create("UTC").Format(null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Zona/Que/No/Existe")]
    public void Constructor_EmptyOrUnknownZone_FallsBackToTheServerZone(string TimeZone)
    {
        var utc = new DateTimeOffset(2026, 3, 5, 14, 30, 0, TimeSpan.Zero);

        var local = Create(TimeZone).Local(utc);

        Assert.Equal(TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local), local);
    }
}
