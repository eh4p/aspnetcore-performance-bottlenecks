using System.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.Models;
using AspNetCorePerformanceBottlenecks.Api.Services;

namespace AspNetCorePerformanceBottlenecks.Api.Endpoints;

public static class LockContentionEndpoints
{
    public static void MapLockContention(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performance/lock-contention/{accountId}");
        group.MapGet("/violation", (int accountId, StatementGenerationService service, HttpContext context,
            ILoggerFactory loggerFactory, CancellationToken ct) => GenerateAsync(accountId, false, service, context, loggerFactory, ct));
        group.MapGet("/solution", (int accountId, StatementGenerationService service, HttpContext context,
            ILoggerFactory loggerFactory, CancellationToken ct) => GenerateAsync(accountId, true, service, context, loggerFactory, ct));
    }

    private static async Task<IResult> GenerateAsync(int accountId, bool solution, StatementGenerationService service,
        HttpContext context, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (DemoAccounts.Validate(accountId) is { } error)
            return error;
        var started = Stopwatch.GetTimestamp();
        var result = solution
            ? await service.SolutionAsync(accountId, cancellationToken)
            : await service.ViolationAsync(accountId, cancellationToken);
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("LockContention"), "lock-contention",
            solution ? "solution" : "violation", started, lockWaitMs: result.WaitMs);
        return Results.Ok(result.Statement);
    }
}
