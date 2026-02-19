using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Normyx.Api.Contracts.Errors;
using Normyx.Api.Middleware;
using Normyx.Api.Endpoints;
using Normyx.Application.Abstractions;
using Normyx.Infrastructure.Audit;
using Normyx.Infrastructure.AI;
using Normyx.Infrastructure.Auth;
using Normyx.Infrastructure.Extensions;
using Normyx.Infrastructure.Integrations;
using Normyx.Infrastructure.Persistence;
using Normyx.Infrastructure.Rag;
using Normyx.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNormyxInfrastructure(builder.Configuration);
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.SectionName));
builder.Services.AddScoped<IObjectStorage, LocalObjectStorage>();
builder.Services.AddScoped<IDocumentTextExtractor, BasicDocumentTextExtractor>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddHttpClient("WebhookPublisher", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<IWebhookPublisher, HttpWebhookPublisher>();

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IPromptTemplateRepository, FilePromptTemplateRepository>();
builder.Services.AddSingleton<IPiiRedactor, RegexPiiRedactor>();
builder.Services.AddHttpClient("AiProvider", client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddTransient<OpenAiCompatibleJsonCompletionProvider>();
builder.Services.AddSingleton<LocalJsonCompletionProvider>();
builder.Services.AddTransient<IAiJsonCompletionProvider, SwitchingJsonCompletionProvider>();
builder.Services.AddSingleton<Normyx.Infrastructure.Compliance.PolicyEngine>();
builder.Services.AddSingleton<IAssessmentExecutionGuard, Normyx.Infrastructure.Compliance.InMemoryAssessmentExecutionGuard>();
builder.Services.AddScoped<IAiDraftService, Normyx.Infrastructure.Compliance.AiDraftService>();
builder.Services.AddScoped<IAssessmentService, Normyx.Infrastructure.Compliance.AssessmentService>();
builder.Services.AddScoped<IExportService, Normyx.Infrastructure.Exports.PdfExportService>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                var httpContext = context.HttpContext;
                var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
                    ? value?.ToString() ?? httpContext.TraceIdentifier
                    : httpContext.TraceIdentifier;

                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                httpContext.Response.ContentType = "application/json";
                var payload = new ApiErrorEnvelope(correlationId, new ApiErrorDetail("unauthorized", "Authentication is required."));
                await httpContext.Response.WriteAsJsonAsync(payload);
            },
            OnForbidden = async context =>
            {
                var httpContext = context.HttpContext;
                var correlationId = httpContext.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value)
                    ? value?.ToString() ?? httpContext.TraceIdentifier
                    : httpContext.TraceIdentifier;

                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = "application/json";
                var payload = new ApiErrorEnvelope(correlationId, new ApiErrorDetail("forbidden", "Insufficient permissions."));
                await httpContext.Response.WriteAsJsonAsync(payload);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("auth", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"auth:{key}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Normyx API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NormyxDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeedAsync(db);
    var ragService = scope.ServiceProvider.GetRequiredService<IRagService>();
    await ragService.SeedReferenceNotesAsync();
}

app.UseAuthentication();
app.UseMiddleware<TenantIsolationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<ApiStatusCodeEnvelopeMiddleware>();
app.UseMiddleware<AuditMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (NormyxDbContext dbContext) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "ready" }) : Results.Problem("Database unavailable");
});

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapAiSystemEndpoints();
app.MapArchitectureEndpoints();
app.MapInventoryEndpoints();
app.MapDocumentEndpoints();
app.MapEvidenceEndpoints();
app.MapAuditEndpoints();
app.MapIntegrationEndpoints();
app.MapQuestionnaireEndpoints();
app.MapAssessmentEndpoints();
app.MapFindingEndpoints();
app.MapActionEndpoints();
app.MapExportEndpoints();
app.MapDashboardEndpoints();

app.Run();

public partial class Program;
