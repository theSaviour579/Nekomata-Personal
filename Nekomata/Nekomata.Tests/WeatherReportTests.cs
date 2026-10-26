using Nekomata.Core.Weather;
using Xunit;

namespace Nekomata.Tests;

public sealed class WeatherReportTests
{
    private const string Sample = """
    {"current":{"time":"2026-10-17T10:00","temperature_2m":11.4,"apparent_temperature":8.2,"weather_code":61,"wind_speed_10m":14.2,"is_day":1},
     "hourly":{"time":["2026-10-17T10:00","2026-10-17T11:00","2026-10-17T16:00","2026-10-17T17:00"],
               "precipitation_probability":[20,30,75,80],"weather_code":[3,3,63,63]},
     "daily":{"time":["2026-10-17","2026-10-18"],"weather_code":[61,2],"temperature_2m_max":[13.1,15.0],"temperature_2m_min":[6.9,8.0],
              "precipitation_probability_max":[80,10],"sunset":["2026-10-17T18:12","2026-10-18T18:10"]}}
    """;

    [Fact]
    public void Parses_open_meteo()
    {
        var report = WeatherReport.Parse(Sample, "Bradford", DateTimeOffset.Now);
        Assert.Equal(11.4, report.TemperatureC);
        Assert.Equal(WeatherKind.Rain, report.Kind);
        Assert.True(report.IsDay);
        Assert.Equal(2, report.TomorrowCode);
        Assert.Equal(new DateTime(2026, 10, 17, 18, 12, 0), report.Sunset);
        Assert.Equal("11° and light rain, feels like 8°. High 13°, low 7°, 80% chance of rain.", report.Summary);
        Assert.Equal(new DateTime(2026, 10, 17, 16, 0, 0), report.RainBetween(new DateTime(2026, 10, 17, 12, 0, 0), new DateTime(2026, 10, 17, 18, 0, 0))?.At);
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear)]
    [InlineData(2, WeatherKind.PartlyCloudy)]
    [InlineData(45, WeatherKind.Fog)]
    [InlineData(53, WeatherKind.Drizzle)]
    [InlineData(81, WeatherKind.Showers)]
    [InlineData(73, WeatherKind.Snow)]
    [InlineData(95, WeatherKind.Thunder)]
    public void Maps_codes(int code, WeatherKind kind) => Assert.Equal(kind, WeatherCodes.Kind(code));

    [Theory]
    [InlineData("BD1 1AA", true)]
    [InlineData("LS12", true)]
    [InlineData("Bradford", false)]
    public void Spots_postcodes(string text, bool expected) => Assert.Equal(expected, WeatherCodes.LooksLikeUkPostcode(text));
}
