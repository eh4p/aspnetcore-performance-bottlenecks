namespace AspNetCorePerformanceBottlenecks.Api.Services;

public sealed class FakeVerificationService
{
    public async Task<string> CheckAsync(int accountId, CancellationToken cancellationToken)
    {
        await Task.Delay(125, cancellationToken);
        return accountId % 5 == 0 ? "ReviewRequired" : "Verified";
    }
}
