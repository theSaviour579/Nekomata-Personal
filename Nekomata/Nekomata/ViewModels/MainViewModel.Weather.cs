using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Nekomata.Core.Weather;
using Nekomata.Models.Planning;

namespace Nekomata.UI.ViewModels;

// Live weather on the Home top line, from Open-Meteo (no key). The location is the town or postcode on the settings page
// (postcodes via postcodes.io, places via Open-Meteo's geocoder), or, if blank, the approximate location of the internet
// connection. Refreshed every 20 minutes. "What's the weather" answers aloud, and half an hour before the end of the
// working day Guardian mentions it once if rain is likely when the user leaves.
public partial class MainViewModel
{
    private static readonly HttpClient WeatherHttp = CreateWeatherHttp();
    private static readonly TimeSpan WeatherRefreshEvery = TimeSpan.FromMinutes(20);

    private (double Lat, double Lon, string Place)? _weatherLocationResolved;
    private WeatherReport? _weather;
    private DateTimeOffset _weatherRetryAt;
    private bool _weatherBusy;
    private DateTime _homeTimeRainWarnedOn;

    [ObservableProperty] private bool hasWeather;
    [ObservableProperty] private WeatherKind weatherKind = WeatherKind.Clear;
    [ObservableProperty] private bool weatherIsDay = true;
    [ObservableProperty] private string weatherTemperature = "";
    [ObservableProperty] private string weatherCondition = "";
    [ObservableProperty] private string weatherDetail = "";
    [ObservableProperty] private string weatherToolTip = "";

    private static HttpClient CreateWeatherHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Nekomata-Guardian/1.0");
        return http;
    }

    private void MaybeRefreshWeather(DateTimeOffset now)
    {
        if (!FeatureOn("weather")) { HasWeather = false; return; }
        if (_weather is null || now - _weather.FetchedAt >= WeatherRefreshEvery) _ = RefreshWeatherAsync();
        else ApplyWeather(_weather);   // keeps day/night right between fetches
        MaybeWarnRainAtHomeTime(now);
    }

    private async Task RefreshWeatherAsync(bool force = false)
    {
        if (_weatherBusy || (!force && DateTimeOffset.Now < _weatherRetryAt)) return;
        _weatherBusy = true;
        try
        {
            _weatherLocationResolved ??= await ResolveWeatherLocationAsync();
            if (_weatherLocationResolved is not { } where) { _weatherRetryAt = DateTimeOffset.Now.AddMinutes(15); return; }
            var json = await WeatherHttp.GetStringAsync(WeatherReport.ForecastUrl(where.Lat, where.Lon));
            _weather = WeatherReport.Parse(json, where.Place, DateTimeOffset.Now);
            ApplyWeather(_weather);
        }
        catch (Exception ex)
        {
            _weatherRetryAt = DateTimeOffset.Now.AddMinutes(10);
            System.Diagnostics.Debug.WriteLine($"Weather unavailable: {ex.Message}");
            if (_weather is null) HasWeather = false;
        }
        finally { _weatherBusy = false; }
    }

    private void ApplyWeather(WeatherReport report)
    {
        var now = DateTime.Now;
        WeatherKind = report.Kind;
        WeatherIsDay = report.Sunset is { } sunset && sunset.Date == now.Date ? now < sunset && now.Hour >= 6 : report.IsDay;
        WeatherTemperature = $"{report.TemperatureC:0}°";
        WeatherCondition = report.Description.ToUpperInvariant();
        WeatherDetail = $"H {report.TodayMaxC:0}° · L {report.TodayMinC:0}°{(report.TodayRainChance >= 30 ? $" · RAIN {report.TodayRainChance}%" : "")}";
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        var rain = report.RainBetween(now, hours.GetEnd(now.Date).AddHours(1));
        WeatherToolTip = $"{report.Place}: {report.Summary}" +
                         (rain is not null ? $"\nRain likely from {rain.At:HH:mm} ({rain.RainChance}%)." : "") +
                         $"\nTomorrow: {WeatherCodes.Describe(report.TomorrowCode).ToLowerInvariant()}, {report.TomorrowMinC:0}–{report.TomorrowMaxC:0}°." +
                         $"\nUpdated {report.FetchedAt.LocalDateTime:HH:mm} · Open-Meteo";
        HasWeather = true;
    }

    private async Task<(double, double, string)?> ResolveWeatherLocationAsync()
    {
        var wanted = Prefs.WeatherLocation.Trim();
        try
        {
            if (wanted.Length > 0 && WeatherCodes.LooksLikeUkPostcode(wanted))
            {
                var outward = wanted.Replace(" ", "").Length <= 4;
                var url = outward
                    ? $"https://api.postcodes.io/outcodes/{Uri.EscapeDataString(wanted.Replace(" ", ""))}"
                    : $"https://api.postcodes.io/postcodes/{Uri.EscapeDataString(wanted.Replace(" ", ""))}";
                using var doc = JsonDocument.Parse(await WeatherHttp.GetStringAsync(url));
                var r = doc.RootElement.GetProperty("result");
                var place = r.TryGetProperty("admin_district", out var district)
                    ? district.ValueKind == JsonValueKind.Array ? district.EnumerateArray().FirstOrDefault().GetString() : district.GetString()
                    : null;
                return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), place ?? wanted.ToUpperInvariant());
            }
            if (wanted.Length > 0)
            {
                using var doc = JsonDocument.Parse(await WeatherHttp.GetStringAsync(
                    $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(wanted)}&count=1&language=en&format=json"));
                if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var r = results[0];
                    return (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), r.GetProperty("name").GetString() ?? wanted);
                }
                Think($"Couldn't find \"{wanted}\" for the weather; using your connection's location");
            }
            // Approximate location of the internet connection.
            foreach (var url in new[] { "https://ipapi.co/json/", "https://ipwho.is/" })
            {
                try
                {
                    using var doc = JsonDocument.Parse(await WeatherHttp.GetStringAsync(url));
                    var r = doc.RootElement;
                    if (r.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number)
                        return (lat.GetDouble(), r.GetProperty("longitude").GetDouble(), r.TryGetProperty("city", out var city) ? city.GetString() ?? "" : "");
                }
                catch (Exception) { /* try the next one */ }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Weather location not found: {ex.Message}"); }
        return null;
    }

    private async Task SayWeatherAsync()
    {
        if (!FeatureOn("weather") && _weather is null) { await RespondAndSpeakAsync("The weather is switched off on the settings page."); return; }
        if (_weather is null || DateTimeOffset.Now - _weather.FetchedAt > TimeSpan.FromMinutes(30)) await RefreshWeatherAsync(force: true);
        if (_weather is not { } w) { await RespondAndSpeakAsync("I can't reach the weather service at the moment."); return; }
        var now = DateTime.Now;
        var hours = _services.GetRequiredService<WorkingDaySettings>();
        var rain = w.RainBetween(now, hours.GetEnd(now.Date).AddHours(1));
        var text = $"{(w.Place.Length > 0 ? $"In {w.Place}, it's " : "It's ")}{w.Summary}" +
                   (rain is not null && w.Kind is not (WeatherKind.Rain or WeatherKind.Drizzle or WeatherKind.Showers or WeatherKind.Thunder)
                       ? $" Rain is likely from about {rain.At:h tt}." : "") +
                   $" Tomorrow, {WeatherCodes.Describe(w.TomorrowCode).ToLowerInvariant()}, up to {w.TomorrowMaxC:0}°.";
        await RespondAndSpeakAsync(text);
    }

    // Once a day, 30 minutes before the end of the working day: rain likely when he leaves.
    private void MaybeWarnRainAtHomeTime(DateTimeOffset now)
    {
        if (_weather is not { } w || OffHoursActive || _homeTimeRainWarnedOn == now.Date) return;
        var end = _services.GetRequiredService<WorkingDaySettings>().GetEnd(now.LocalDateTime.Date);
        var local = now.LocalDateTime;
        if (local < end.AddMinutes(-30) || local > end) return;
        _homeTimeRainWarnedOn = now.Date;
        var rain = w.RainBetween(end.AddMinutes(-30), end.AddHours(1), threshold: 65);
        if (rain is null) return;
        _ = SpeakGuardianAsync($"Heads up: rain is likely around home time, {rain.RainChance}% from {rain.At:h tt}. Worth grabbing a coat.");
    }
}
