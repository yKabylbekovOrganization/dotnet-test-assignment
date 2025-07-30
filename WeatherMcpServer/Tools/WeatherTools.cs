using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
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
        _apiKey = "6b8ee00c1cd04d3e909150103253007"
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

            var url = $"https://api.openweathermap.org/data/2.5/forecast?q={query}&appid={_apiKey}&units=metric";
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                return $"Forecast fetch failed: {response.StatusCode}";
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var list = doc.RootElement.GetProperty("list");

            var forecast = new List<string>();
            int counter = 0;

            foreach (var item in list.EnumerateArray())
            {
                var dateTime = item.GetProperty("dt_txt").GetString();
                var temp = item.GetProperty("main").GetProperty("temp").GetDouble();
                var desc = item.GetProperty("weather")[0].GetProperty("description").GetString();

                if (dateTime != null && dateTime.Contains("12:00:00")) // Прогноз в полдень
                {
                    forecast.Add($"{dateTime[..10]}: {temp}°C, {desc}");
                    counter++;
                }

                if (counter >= 3)
                    break;
            }

            return $"3-day forecast for {city}:\n" + string.Join("\n", forecast);
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

            // 1. Geocoding API — Get coordinates
            var geoUrl = $"http://api.openweathermap.org/geo/1.0/direct?q={query}&limit=1&appid={_apiKey}";
            var geoResponse = await client.GetAsync(geoUrl);

            if (!geoResponse.IsSuccessStatusCode)
                return $"Failed to get coordinates: {geoResponse.StatusCode}";

            var geoJson = await geoResponse.Content.ReadAsStringAsync();
            var geoDoc = JsonDocument.Parse(geoJson);
            var geoRoot = geoDoc.RootElement;

            if (geoRoot.GetArrayLength() == 0)
                return $"No coordinates found for city '{city}'";

            var location = geoRoot[0];
            var lat = location.GetProperty("lat").GetDouble();
            var lon = location.GetProperty("lon").GetDouble();

            // 2. OneCall API — Get alerts
            var alertsUrl = $"https://api.openweathermap.org/data/3.0/onecall?lat={lat}&lon={lon}&appid={_apiKey}&units=metric";
            var alertResponse = await client.GetAsync(alertsUrl);

            if (!alertResponse.IsSuccessStatusCode)
                return $"Failed to get weather alerts: {alertResponse.StatusCode}";

            var alertJson = await alertResponse.Content.ReadAsStringAsync();
            var alertDoc = JsonDocument.Parse(alertJson);
            var root = alertDoc.RootElement;

            if (!root.TryGetProperty("alerts", out var alertsArray))
                return $"No weather alerts for {city}.";

            var alerts = alertsArray.EnumerateArray()
                .Select(alert =>
                {
                    var eventName = alert.GetProperty("event").GetString();
                    var sender = alert.GetProperty("sender_name").GetString();
                    var description = alert.GetProperty("description").GetString();
                    return $"⚠️ {eventName} by {sender}\n{description}";
                });

            return $"Weather alerts for {city}:\n\n" + string.Join("\n\n", alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get weather alerts");
            return $"Error: {ex.Message}";
        }
    }
}
