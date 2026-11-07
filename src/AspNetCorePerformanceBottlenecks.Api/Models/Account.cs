namespace AspNetCorePerformanceBottlenecks.Api.Models;

public sealed class Account
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string Currency { get; set; } = "USD";
    public List<Transaction> Transactions { get; set; } = [];
}

public sealed class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public long AmountMinor { get; set; }
    public required string Reference { get; set; }
}
