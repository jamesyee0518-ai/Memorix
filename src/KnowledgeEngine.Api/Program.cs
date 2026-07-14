using System.Text;
using KnowledgeEngine.Application;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Mcp;
using KnowledgeEngine.Infrastructure.Reports;
using KnowledgeEngine.Api.Middlewares;
using KnowledgeEngine.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

var isMcpMode = args.Contains("--mcp");

var builder = WebApplication.CreateBuilder(args);

// Serilog — in MCP mode, redirect logs to stderr to keep stdout clean for JSON-RPC
builder.Host.UseSerilog((ctx, lc) =>
{
    if (isMcpMode)
    {
        // MCP mode: send all logs to stderr to keep stdout clean for JSON-RPC
        lc.MinimumLevel.Information()
          .Enrich.FromLogContext()
          .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose);
    }
    else
    {
        lc.ReadFrom.Configuration(ctx.Configuration)
          .Enrich.FromLogContext()
          .WriteTo.Console();
    }
});

// Configuration
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:3000" };

// Add services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Controllers
builder.Services.AddControllers();

// Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Knowledge Engine API",
        Version = "v1",
        Description = "Knowledge Engine Backend API"
    });

    // Automatically infer security schemes from ASP.NET Core authentication (JWT Bearer)
    c.InferSecuritySchemes();
});

// Authentication: JWT for cloud accounts, local loopback identity for local-first desktop use.
var key = Encoding.UTF8.GetBytes(jwtSettings.Secret);
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "LocalOrJwt";
        options.DefaultChallengeScheme = "LocalOrJwt";
    })
    .AddPolicyScheme("LocalOrJwt", "Local or JWT", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : LocalAuthenticationHandler.SchemeName;
        };
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.Zero
        };
    })
    .AddScheme<AuthenticationSchemeOptions, LocalAuthenticationHandler>(
        LocalAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddAuthorization();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        db.Database.EnsureCreated();
        db.EnsureMultilingualSetupAsync().GetAwaiter().GetResult();
        scope.ServiceProvider.GetRequiredService<IChineseFullTextIndexService>()
            .EnsureCreatedAsync().GetAwaiter().GetResult();
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            // PostgreSQL deployments need provider-specific vector and compatibility setup.
            db.EnsureVectorSetupAsync().GetAwaiter().GetResult();
            db.EnsureDualModeSetupAsync().GetAwaiter().GetResult();
        }
        // Initialize system report templates (Phase 4)
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SystemReportTemplates>>();
        SystemReportTemplates.InitializeAsync(db, logger).GetAwaiter().GetResult();

        var localUser = db.Users.FirstOrDefault(u =>
            u.Id == LocalUserConstants.UserId || u.Email == LocalUserConstants.Email);
        if (localUser == null)
        {
            var now = DateTime.UtcNow;
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            db.Users.Add(new User
            {
                Id = LocalUserConstants.UserId,
                Email = LocalUserConstants.Email,
                Nickname = LocalUserConstants.Nickname,
                PasswordHash = passwordHasher.HashPassword(Guid.NewGuid().ToString("N")),
                PlanCode = "local",
                Status = "active",
                Timezone = "Asia/Shanghai",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.SaveChanges();
        }
        else
        {
            localUser.Nickname = LocalUserConstants.Nickname;
            localUser.PlanCode = "local";
            localUser.Status = "active";
            localUser.UpdatedAt = DateTime.UtcNow;
            db.SaveChanges();
        }

        if (app.Environment.IsDevelopment())
        {
            const string testEmail = "test@example.com";
            if (!db.Users.Any(u => u.Email == testEmail))
            {
                var now = DateTime.UtcNow;
                var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Email = testEmail,
                    Nickname = "测试用户",
                    PasswordHash = passwordHasher.HashPassword("12345678"),
                    PlanCode = "free",
                    Status = "active",
                    Timezone = "Asia/Shanghai",
                    CreatedAt = now,
                    UpdatedAt = now
                });
                db.SaveChanges();
            }
        }
    }
    catch (Exception ex)
    {
        Log.Logger.Fatal(ex, "Database initialization failed. Stopping startup.");
        throw;
    }
}

// MCP Server mode — run stdio JSON-RPC loop and exit (no HTTP server)
if (isMcpMode)
{
    var mcpServer = app.Services.GetRequiredService<McpServer>();
    await mcpServer.RunAsync();
    return;
}

// Middlewares
app.UseMiddleware<TraceIdMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Knowledge Engine API v1");
});

// CORS
app.UseCors();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Agent API Key authentication middleware (after JWT auth, handles /api/agent/ paths)
app.UseMiddleware<AgentAuthMiddleware>();

// Controllers
app.MapControllers();

// Health check
app.MapGet("/health", async (AppDbContext db, CancellationToken ct) =>
{
    try
    {
        await db.Users.Take(1).CountAsync(ct);
        return Results.Ok(new { status = "healthy", database = "ready", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new
            {
                status = "unhealthy",
                database = "not_ready",
                error = app.Environment.IsDevelopment() ? ex.Message : "Database is not ready",
                timestamp = DateTime.UtcNow
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();
