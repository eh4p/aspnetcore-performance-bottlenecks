namespace AspNetCorePerformanceBottlenecks.Api.Models;

public static class DemoAccounts
{
    public const int Count = 20;

    public static bool IsKnown(int accountId) => accountId is >= 1 and <= Count;

    public static string Name(int accountId) => $"Demo Account {accountId:D2}";

    public static IResult? Validate(int accountId) => accountId <= 0
        ? Results.Problem(statusCode: 400, title: "accountId must be a positive integer.")
        : !IsKnown(accountId)
            ? Results.Problem(statusCode: 404, title: "Unknown synthetic account. Use IDs 1–20.")
            : null;
}
