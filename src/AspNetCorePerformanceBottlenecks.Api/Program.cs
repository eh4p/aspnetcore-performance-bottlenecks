using AspNetCorePerformanceBottlenecks.Api.Data;
using AspNetCorePerformanceBottlenecks.Api.Endpoints;
using AspNetCorePerformanceBottlenecks.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
// Load user secrets in this local-only showcase even in Release/Production environment.
builder.Configuration.AddUserSecrets<Program>(optional: true).AddEnvironmentVariables().AddCommandLine(args);
var connectionString = builder.Configuration.GetConnectionString("FintechDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings:FintechDb using user secrets or ConnectionStrings__FintechDb. See README.");
var connection = new NpgsqlConnectionStringBuilder(connectionString);
if (connection.Host is not ("localhost" or "127.0.0.1" or "::1"))
    throw new InvalidOperationException("This showcase requires PostgreSQL on localhost/127.0.0.1/::1.");

builder.Services.AddProblemDetails();
builder.Services.AddScoped<DbCommandCountingInterceptor>();
builder.Services.AddDbContext<FintechDbContext>((services, options) => options
    .UseNpgsql(connectionString)
    .AddInterceptors(services.GetRequiredService<DbCommandCountingInterceptor>()));
builder.Services.AddSingleton<FakeVerificationService>();
builder.Services.AddSingleton<StatementGenerationService>();
builder.Services.AddSingleton<DuplicateReferenceService>();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
// Initialize immutable CPU inputs and all semaphore gates before requests and warmup.
_ = app.Services.GetRequiredService<DuplicateReferenceService>();
_ = app.Services.GetRequiredService<StatementGenerationService>();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FintechDbContext>();
    await DemoDataSeeder.SeedAsync(db, app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AspNetCorePerformanceBottlenecks.Api.Data.DemoDataSeeder"),
        app.Lifetime.ApplicationStopping);
}

app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapBlockingIo();
app.MapNPlusOne();
app.MapLockContention();
app.MapCpuHotspot();
await app.RunAsync();
