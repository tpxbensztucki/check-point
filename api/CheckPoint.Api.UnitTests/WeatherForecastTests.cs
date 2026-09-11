namespace CheckPoint.Api.UnitTests;

// Example unit test — exercises pure logic in the API project directly, no HTTP,
// no database. Replace/extend once real domain logic exists (Milestone 2+).
public class WeatherForecastTests
{
    [Fact]
    public void TemperatureF_ConvertsFromCelsius()
    {
        var forecast = new WeatherForecast(DateOnly.FromDateTime(DateTime.Today), 0, "Test");

        Assert.Equal(32, forecast.TemperatureF);
    }
}
