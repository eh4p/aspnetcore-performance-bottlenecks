namespace AspNetCorePerformanceBottlenecks.Api.DTOs;

public sealed record VerificationSummary(int AccountId, string AccountName, string Currency, string VerificationStatus);
public sealed record AccountSummary(int AccountId, string AccountName, string Currency, long TotalAmountMinor);
public sealed record StatementSummary(int AccountId, string StatementReference, int TransactionCount, long TotalAmountMinor);
public sealed record DuplicateReferenceSummary(int AccountId, int ReferenceCount, int DuplicateReferenceCount);
