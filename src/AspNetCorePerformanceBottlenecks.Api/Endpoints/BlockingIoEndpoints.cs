using System.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.DTOs;
using AspNetCorePerformanceBottlenecks.Api.Models;
using AspNetCorePerformanceBottlenecks.Api.Services;

namespace AspNetCorePerformanceBottlenecks.Api.Endpoints;

public static class BlockingIoEndpoints
{
    public static void MapBlockingIo(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performance/blocking-io/{accountId}");
        group.MapGet("/violation", Violation);
        group.MapGet("/solution", SolutionAsync);
    }

    private static IResult Violation(int accountId, FakeVerificationService service, HttpContext context,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (DemoAccounts.Validate(accountId) is { } error)
            return error;
        var started = Stopwatch.GetTimestamp();
        // VIOLATION: a request worker is blocked while the asynchronous dependency is pending.
        var status = service.CheckAsync(accountId, cancellationToken).GetAwaiter().GetResult();
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("BlockingIo"), "blocking-io", "violation", started);
        return Results.Ok(new VerificationSummary(accountId, DemoAccounts.Name(accountId), "USD", status));
    }

    private static async Task<IResult> SolutionAsync(int accountId, FakeVerificationService service, HttpContext context,
        ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (DemoAccounts.Validate(accountId) is { } error)
            return error;
        var started = Stopwatch.GetTimestamp();
        // SOLUTION: release the request worker during the same asynchronous dependency wait.
        var status = await service.CheckAsync(accountId, cancellationToken);
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("BlockingIo"), "blocking-io", "solution", started);
        return Results.Ok(new VerificationSummary(accountId, DemoAccounts.Name(accountId), "USD", status));
    }
}
