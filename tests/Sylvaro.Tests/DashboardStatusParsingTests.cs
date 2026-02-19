using System.Text.Json;
using Sylvaro.Web.Models;

namespace Sylvaro.Tests;

public class DashboardStatusParsingTests
{
    [Fact]
    public void DashboardSystemDto_StatusNumeric_MapsToUnknownLifecycle()
    {
        const string payload = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "LoanAssist",
          "status": 5,
          "assessmentsCount": 3,
          "scoreTrend": []
        }
        """;

        var parsed = JsonSerializer.Deserialize<DashboardSystemDto>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(parsed);
        Assert.Equal("5", parsed!.Status);
        Assert.Equal("Unknown", StatusValueNormalizer.NormalizeSystemStatus(parsed.Status));
    }

    [Fact]
    public void DashboardSystemDto_StatusString_PreservesKnownStatus()
    {
        const string payload = """
        {
          "id": "11111111-1111-1111-1111-111111111111",
          "name": "LoanAssist",
          "status": "Active",
          "assessmentsCount": 3,
          "scoreTrend": []
        }
        """;

        var parsed = JsonSerializer.Deserialize<DashboardSystemDto>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(parsed);
        Assert.Equal("Active", parsed!.Status);
        Assert.Equal("Active", StatusValueNormalizer.NormalizeSystemStatus(parsed.Status));
    }
}
