using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Normyx.Tests;

public class TenantIsolationTests : IAsyncLifetime
{
    private readonly string _testDatabaseName = $"normyx_test_{Guid.NewGuid():N}";
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<Program> _factory = null!;
    private bool _dockerAvailable = true;

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase(_testDatabaseName)
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgres.StartAsync();
            _factory = new CustomFactory(_postgres.GetConnectionString());
        }
        catch (DockerUnavailableException)
        {
            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dockerAvailable)
        {
            _factory.Dispose();
            if (_postgres is not null)
            {
                await _postgres.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task TenantA_CannotSee_TenantB_System()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        var tenantAToken = await LoginAsync(client, "NordicFin AB", "admin@nordicfin.example", "ChangeMe123!");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantBRegistration = await client.PostAsJsonAsync("/auth/register", new
        {
            tenantName = $"OtherTenant-{suffix} AB",
            email = $"admin+{suffix}@othertenant.example",
            displayName = "Other Admin",
            password = "ChangeMe123!"
        });
        tenantBRegistration.EnsureSuccessStatusCode();

        var tenantBAuth = await tenantBRegistration.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(tenantBAuth);

        using var tenantBRequest = new HttpRequestMessage(HttpMethod.Post, "/aisystems")
        {
            Content = JsonContent.Create(new
            {
                name = "TenantB Secret System",
                description = "Should not leak",
                ownerUserId = (Guid?)null
            })
        };
        tenantBRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantBAuth!.AccessToken);
        var tenantBCreateResult = await client.SendAsync(tenantBRequest);
        tenantBCreateResult.EnsureSuccessStatusCode();

        using var tenantAListRequest = new HttpRequestMessage(HttpMethod.Get, "/aisystems");
        tenantAListRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantAToken);
        var tenantAListResponse = await client.SendAsync(tenantAListRequest);
        tenantAListResponse.EnsureSuccessStatusCode();

        var payload = await tenantAListResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("TenantB Secret System", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TenantAdmin_CanListRoles()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        var token = await LoginAsync(client, "NordicFin AB", "admin@nordicfin.example", "ChangeMe123!");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/tenants/roles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ComplianceOfficer", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SecurityLead", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductOwner", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Auditor", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ActionReview_UpdatesStatus_AndCreatesReviewHistory()
    {
        if (!_dockerAvailable)
        {
            return;
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost")
        });

        var token = await LoginAsync(client, "NordicFin AB", "admin@nordicfin.example", "ChangeMe123!");

        var systemListResponse = await SendAuthorizedAsync(client, token, HttpMethod.Get, "/aisystems");
        systemListResponse.EnsureSuccessStatusCode();
        var systemListPayload = await systemListResponse.Content.ReadAsStringAsync();
        var systemId = ReadGuidByName(systemListPayload, "LoanAssist");

        var versionListResponse = await SendAuthorizedAsync(client, token, HttpMethod.Get, $"/aisystems/{systemId}/versions");
        versionListResponse.EnsureSuccessStatusCode();
        var versionListPayload = await versionListResponse.Content.ReadAsStringAsync();
        var versionId = ReadFirstGuid(versionListPayload, "id");

        var assessmentRunResponse = await SendAuthorizedAsync(client, token, HttpMethod.Post, $"/versions/{versionId}/assessments/run", new { });
        assessmentRunResponse.EnsureSuccessStatusCode();

        var boardResponse = await SendAuthorizedAsync(client, token, HttpMethod.Get, $"/actions/board/{versionId}");
        boardResponse.EnsureSuccessStatusCode();
        var boardPayload = await boardResponse.Content.ReadAsStringAsync();
        var actionId = ReadFirstActionIdFromBoard(boardPayload);

        var reviewResponse = await SendAuthorizedAsync(client, token, HttpMethod.Post, $"/actions/{actionId}/reviews", new
        {
            decision = 1,
            comment = "Integration test approval"
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, reviewResponse.StatusCode);

        var reviewsResponse = await SendAuthorizedAsync(client, token, HttpMethod.Get, $"/actions/{actionId}/reviews");
        reviewsResponse.EnsureSuccessStatusCode();
        var reviewsPayload = await reviewsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Approved", reviewsPayload, StringComparison.OrdinalIgnoreCase);

        var actionsResponse = await SendAuthorizedAsync(client, token, HttpMethod.Get, $"/actions/version/{versionId}");
        actionsResponse.EnsureSuccessStatusCode();
        var actionsPayload = await actionsResponse.Content.ReadAsStringAsync();
        Assert.Contains(actionId.ToString(), actionsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Done", actionsPayload, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> LoginAsync(HttpClient client, string tenant, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            tenantName = tenant,
            email,
            password
        });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth!.AccessToken;
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, string token, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static Guid ReadFirstGuid(string json, string propertyName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Expected a non-empty array for '{propertyName}'.");
        }

        var first = root[0];
        if (!first.TryGetProperty(propertyName, out var idProp))
        {
            throw new InvalidOperationException($"Property '{propertyName}' was not found.");
        }

        return idProp.GetGuid();
    }

    private static Guid ReadGuidByName(string json, string systemName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Expected an array of systems.");
        }

        foreach (var item in root.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameProperty))
            {
                continue;
            }

            var name = nameProperty.GetString();
            if (!string.Equals(name, systemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.TryGetProperty("id", out var idProperty))
            {
                return idProperty.GetGuid();
            }
        }

        throw new InvalidOperationException($"System '{systemName}' was not found.");
    }

    private static Guid ReadFirstActionIdFromBoard(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var lane in new[] { "new", "inProgress", "done", "acceptedRisk" })
        {
            if (!root.TryGetProperty(lane, out var actions) || actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() == 0)
            {
                continue;
            }

            var first = actions[0];
            if (first.TryGetProperty("id", out var idProp))
            {
                return idProp.GetGuid();
            }
        }

        throw new InvalidOperationException("No action item found in any board lane.");
    }

    private sealed record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

    private sealed class CustomFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["UseHttpsRedirection"] = "false",
                    ["ConnectionStrings:DefaultConnection"] = connectionString
                });
            });
        }
    }
}
