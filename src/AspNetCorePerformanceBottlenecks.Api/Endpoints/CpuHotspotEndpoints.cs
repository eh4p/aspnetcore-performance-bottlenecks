using System.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.Diagnostics;
using AspNetCorePerformanceBottlenecks.Api.DTOs;
using AspNetCorePerformanceBottlenecks.Api.Models;
using AspNetCorePerformanceBottlenecks.Api.Services;

namespace AspNetCorePerformanceBottlenecks.Api.Endpoints;

public static class CpuHotspotEndpoints
{
    public static void MapCpuHotspot(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/performance/cpu-hotspot/{accountId}");
        group.MapGet("/violation", (int accountId, DuplicateReferenceService service, HttpContext context,
            ILoggerFactory loggerFactory, CancellationToken ct) => Calculate(accountId, false, service, context, loggerFactory, ct));
        group.MapGet("/solution", (int accountId, DuplicateReferenceService service, HttpContext context,
            ILoggerFactory loggerFactory, CancellationToken ct) => Calculate(accountId, true, service, context, loggerFactory, ct));
    }

    private static IResult Calculate(int accountId, bool solution, DuplicateReferenceService service,
        HttpContext context, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (DemoAccounts.Validate(accountId) is { } error)
            return error;
        var started = Stopwatch.GetTimestamp();
        var result = solution ? service.Solution(accountId, cancellationToken) : service.Violation(accountId, cancellationToken);
        DemoDiagnostics.Complete(context, loggerFactory.CreateLogger("CpuHotspot"), "cpu-hotspot",
            solution ? "solution" : "violation", started, operations: result.Operations);
        return Results.Ok(new DuplicateReferenceSummary(accountId, DuplicateReferenceService.ReferenceCount, result.Count));
    }
}
