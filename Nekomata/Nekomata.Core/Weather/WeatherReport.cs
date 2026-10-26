using System.Globalization;
using System.Text.Json;

namespace Nekomata.Core.Weather;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Showers, Snow, Thunder }

public sealed record WeatherHour(DateTime At, int RainChance, int Code);

/// <summary>Current conditions and today's outlook, from Open-Meteo.</summary>
public sealed record WeatherReport(
    string Place, double TemperatureC, double FeelsLikeC, int Code, bool IsDay, double WindMph,
    double TodayMaxC, double TodayMinC, int TodayRainChance, int TomorrowCode, double TomorrowMaxC, double TomorrowMinC,
    DateTime? Sunset, IReadOnlyList<WeatherHour> Hours, DateTimeOffset FetchedAt)
{
    public WeatherKind Kind => WeatherCodes.Kind(Code);
    public string Description => WeatherCodes.Describe(Code);

    /// <summary>The first hour from <paramref name="from"/> until <paramref name="until"/> with at least <paramref name="threshold"/>% chance of rain.</summary>
    public WeatherHour? RainBetween(DateTime from, DateTime until, int threshold = 60) =>
        Hours.FirstOrDefault(h => h.At >= from.AddMinutes(-59) && h.At < until && h.RainChance >= threshold);

    /// <summary>"11° and light rain, feels like 8°. High 13°, 70% chance of rain."</summary>
    public string Summary =>
        $"{TemperatureC:0}° and {Description.ToLowerInvariant()}{(Math.Abs(FeelsLikeC - TemperatureC) >= 2 ? $", feels like {FeelsLikeC:0}°" : "")}. " +
        $"High {TodayMaxC:0}°, low {TodayMinC:0}°{(TodayRainChance >= 30 ? $", {TodayRainChance}% chance of rain" : "")}.";

    public static WeatherReport Parse(string json, string place, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var current = root.GetProperty("current");
        var daily = root.GetProperty("daily");
        double D(JsonElement e, string name, int i = -1) =>
            i < 0 ? (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0)
                  : e.TryGetProperty(name, out var a) && a.GetArrayLength() > i && a[i].ValueKind == JsonValueKind.Number ? a[i].GetDouble() : 0;
        DateTime? T(string? text) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

        var hours = new List<WeatherHour>();
        if (root.TryGetProperty("hourly", out var hourly) && hourly.TryGetProperty("time", out var times))
        {
            for (var i = 0; i < times.GetArrayLength(); i++)
                if (T(times[i].GetString()) is { } at)
                    hours.Add(new(at, (int)D(hourly, "precipitation_probability", i), (int)D(hourly, "weather_code", i)));
        }
        DateTime? sunset = daily.TryGetProperty("sunset", out var sunsets) && sunsets.GetArrayLength() > 0 ? T(sunsets[0].GetString()) : null;

        return new WeatherReport(place,
            D(current, "temperature_2m"), D(current, "apparent_temperature"), (int)D(current, "weather_code"), D(current, "is_day") >= 1,
            D(current, "wind_speed_10m"),
            D(daily, "temperature_2m_max", 0), D(daily, "temperature_2m_min", 0), (int)D(daily, "precipitation_probability_max", 0),
            (int)D(daily, "weather_code", 1), D(daily, "temperature_2m_max", 1), D(daily, "temperature_2m_min", 1),
            sunset, hours, fetchedAt);
    }

    public static string ForecastUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude:0.####}&longitude={longitude:0.####}") +
            "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day" +
            "&hourly=precipitation_probability,weather_code" +
            "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunset" +
            "&timezone=auto&forecast_days=2&wind_speed_unit=mph";
}

/// <summary>WMO weather interpretation codes as Open-Meteo uses them.</summary>
public static class WeatherCodes
{
    public static WeatherKind Kind(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        >= 61 and <= 67 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 80 and <= 82 => WeatherKind.Showers,
        >= 95 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy
    };

    public static string Describe(int code) => code switch
    {
        0 => "Clear",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 => "Fog",
        48 => "Freezing fog",
        51 => "Light drizzle",
        53 => "Drizzle",
        55 => "Heavy drizzle",
        56 or 57 => "Freezing drizzle",
        61 => "Light rain",
        63 => "Rain",
        65 => "Heavy rain",
        66 or 67 => "Freezing rain",
        71 => "Light snow",
        73 => "Snow",
        75 => "Heavy snow",
        77 => "Snow grains",
        80 => "Light showers",
        81 => "Showers",
        82 => "Heavy showers",
        85 => "Snow showers",
        86 => "Heavy snow showers",
        95 => "Thunderstorms",
        96 or 99 => "Thunderstorms with hail",
        _ => "Cloudy"
    };

    /// <summary>A UK postcode (full or outward), e.g. "BD1 1AA" or "LS12".</summary>
    public static bool LooksLikeUkPostcode(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^[A-Za-z]{1,2}\d[A-Za-z\d]?(\s*\d[A-Za-z]{2})?$");
}
