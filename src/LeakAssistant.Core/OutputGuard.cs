namespace LeakAssistant.Core;

public sealed record GuardResult(bool Allowed, string Reason);

/// <summary>
/// A deterministic check after the model answers. Every £ amount in the answer must be one the tools trusted
/// in this conversation: found in an official policy extract, or produced by calculate_allowance.
/// Anything else is blocked, including amounts copied from community content.
/// </summary>
public static class OutputGuard
{
    public static GuardResult Check(string answer, LeakTools tools)
    {
        List<decimal> unsupported = Money.FindAmounts(answer)
            .Where(amount => !tools.TrustedAmounts.Contains(amount))
            .Distinct()
            .ToList();

        return unsupported.Count == 0
            ? new GuardResult(true, "")
            : new GuardResult(false, $"Blocked: {string.Join(", ", unsupported.Select(a => $"£{a:N2}"))} did not come from official policy or the calculator.");
    }
}