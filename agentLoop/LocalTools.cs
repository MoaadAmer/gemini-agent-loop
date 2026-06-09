using Microsoft.Extensions.Logging;

public class LocalTools
{
    private readonly ILogger<LocalTools> _logger;

    public LocalTools(ILogger<LocalTools> logger)
    {
        _logger = logger;
    }
    public string GetWeather(string city)
    {

        _logger.LogInformation("[TOOL EXECUTION] -> Running GetWeather() for '{city}'...", city);
        city = city.ToLower();
        return city switch
        {
            "tokyo" => "Sunny, 26°C",
            "london" => "Rainy, 14°C",
            _ => "Cloudy, 20°C",
        };
    }
    public string GetCurrentTime()
    {
        _logger.LogInformation("[TOOL EXECUTION] -> Running GetCurrentTime()...");
        return DateTime.Now.ToString("HH:mm:ss");
    }

}
