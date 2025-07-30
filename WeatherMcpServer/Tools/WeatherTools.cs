using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WeatherMcpServer.Tools;

public class WeatherTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeatherTools> _logger;
    private readonly string _apiKey;

    public WeatherTools(IHttpClientFactory httpClientFactory, ILogger<WeatherTools> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("WEATHER_API_KEY")
            ?? throw new InvalidOperationException("WEATHER_API_KEY environment variable is not set");
    }

    [McpServerTool]
    [Description("Get current weather using WeatherAPI.com.")]
    public async Task<string> GetCurrentWeather(
    [Description("City name")] string city,
    [Description("Optional country code (e.g. 'KZ')")] string? countryCode = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var query = $"{city}{(countryCode != null ? $",{countryCode}" : "")}";
            var url = $"https://api.weatherapi.com/v1/current.json?key={_apiKey}&q={Uri.EscapeDataString(query)}";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"Error fetching weather: {response.StatusCode}";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var location = root.GetProperty("location").GetProperty("name").GetString();
            var tempC = root.GetProperty("current").GetProperty("temp_c").GetDouble();
            var condition = root.GetProperty("current").GetProperty("condition").GetProperty("text").GetString();

            return $"Current weather in {location}: {tempC}°C, {condition}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get weather");
            return $"Error: {ex.Message}";
        }
    }


    [McpServerTool]
    [Description("Get 3-day weather forecast for the specified city.")]
    public async Task<string> GetWeatherForecast(
    [Description("City name")] string city,
    [Description("Optional country code (e.g. 'KZ', 'US')")] string? countryCode = null)
    {
        try
        {
            var query = $"{city}{(countryCode != null ? $",{countryCode}" : "")}";
            var client = _httpClientFactory.CreateClient();

            var url = $"https://api.weatherapi.com/v1/forecast.json?key={_apiKey}&q={Uri.EscapeDataString(query)}&days=3";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return $"Forecast fetch failed: {response.StatusCode}";
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var forecastDays = doc.RootElement.GetProperty("forecast").GetProperty("forecastday");

            var forecast = new List<string>();

            foreach (var day in forecastDays.EnumerateArray())
            {
                var date = day.GetProperty("date").GetString();
                var temp = day.GetProperty("day").GetProperty("avgtemp_c").GetDouble();
                var desc = day.GetProperty("day").GetProperty("condition").GetProperty("text").GetString();
                forecast.Add($"{date}: {temp}°C, {desc}");
            }

            var location = doc.RootElement.GetProperty("location").GetProperty("name").GetString();
            return $"3-day forecast for {location}:\n" + string.Join("\n", forecast);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch forecast");
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool]
    [Description("Get weather alerts for the specified city (if available).")]
    public async Task<string> GetWeatherAlerts(
    [Description("City name")] string city,
    [Description("Optional country code (e.g. 'KZ', 'US')")] string? countryCode = null)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var query = $"{city}{(countryCode != null ? $",{countryCode}" : "")}";

            var url = $"https://api.weatherapi.com/v1/alerts.json?key={_apiKey}&q={Uri.EscapeDataString(query)}";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"Failed to get weather alerts: {response.StatusCode}";

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("alerts", out var alertsObj) ||
                !alertsObj.TryGetProperty("alert", out var alertsArray) ||
                alertsArray.GetArrayLength() == 0)
            {
                return $"No weather alerts for {city}.";
            }

            var alerts = alertsArray.EnumerateArray().Select(alert =>
            {
                var eventName = alert.TryGetProperty("event", out var ev) ? ev.GetString() : alert.GetProperty("headline").GetString();
                var desc = alert.GetProperty("desc").GetString();
                return $"⚠️ {eventName}\n{desc}";
            });

            var location = query;
            return $"Weather alerts for {location}:\n\n" + string.Join("\n\n", alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get weather alerts");
            return $"Error: {ex.Message}";
        }
    }
}
