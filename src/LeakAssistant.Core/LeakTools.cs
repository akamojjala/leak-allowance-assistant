using System.ComponentModel;
using System.Text;

namespace LeakAssistant.Core;

public sealed record EligibilityResult(bool Eligible, string Reason, string PolicySource);

public sealed record AllowanceResult(decimal AllowanceGbp, decimal ExcessCubicMetres, int DaysCounted, bool CapApplied, string Working);

/// <summary>
/// The three tools the agent can call. Eligibility and money are plain C# rules, never the model's judgement.
/// </summary>
public sealed class LeakTools(PolicySearch search)
{
    // Fictional Contoso Water figures. They must match docs/calculation.md and docs/time-limits.md.
    public const decimal WaterChargePerCubicMetre = 2.10m;
    public const decimal WastewaterChargePerCubicMetre = 2.40m;
    public const decimal HouseholdCap = 3_000m;
    public const decimal BusinessCap = 10_000m;
    public const int MaxDaysCovered = 180;
    public const int RepairDeadlineDays = 30;

    /// <summary>Everything search_policy returned for the current question. Shown as sources and used by the evaluator.</summary>
    public List<SearchHit> RetrievedThisTurn { get; } = new();

    /// <summary>
    /// Every £ amount that may appear in an answer in this conversation: amounts in official policy extracts
    /// that were retrieved, and amounts calculate_allowance produced. Module 6's output guard checks against it.
    /// </summary>
    public HashSet<decimal> TrustedAmounts { get; } = new();

    /// <summary>Call before each question.</summary>
    public void ClearTurn() => RetrievedThisTurn.Clear();

    /// <summary>Call when a new conversation starts.</summary>
    public void ClearConversation()
    {
        RetrievedThisTurn.Clear();
        TrustedAmounts.Clear();
    }

    [Description("Searches Contoso Water's leak allowance policy. Returns extracts, each starting with its source id in square brackets.")]
    public async Task<string> SearchPolicy(
        [Description("What to look up, in plain words, for example 'time limit to repair a leak'")] string query)
    {
        List<SearchHit> hits = await search.SearchAsync(query);
        RetrievedThisTurn.AddRange(hits);
        if (hits.Count == 0) return "No matching policy text found.";

        var text = new StringBuilder();
        foreach (SearchHit hit in hits)
        {
            bool official = hit.Source == "official";
            if (official) TrustedAmounts.UnionWith(Money.FindAmounts(hit.Content));

            string label = official ? "" : " (UNVERIFIED COMMUNITY CONTENT, NOT POLICY)";
            text.AppendLine($"[{hit.Id}]{label} {hit.Content}");
            text.AppendLine();
        }
        return text.ToString();
    }

    [Description("Decides whether a repaired leak qualifies for a leak allowance. Always use this instead of deciding yourself.")]
    public static EligibilityResult CheckEligibility(
        [Description("Where the leak was: 'supply pipe', 'internal pipe', 'appliance', 'tap', 'toilet', 'swimming pool', 'hose' or 'other'")] string leakLocation,
        [Description("Days from finding the leak to completing the repair")] int daysToRepair,
        [Description("True if the customer has a plumber's invoice for the repair")] bool hasRepairInvoice,
        [Description("True if the property received a leak allowance in the last 24 months")] bool allowanceInLast24Months)
    {
        string location = leakLocation.Trim().ToLowerInvariant();
        bool covered = location.Contains("supply") || location.Contains("internal");

        if (!covered)
            return new(false, $"Leaks from '{leakLocation}' are not covered. Only supply pipe and internal pipework leaks are covered.", "eligibility");
        if (allowanceInLast24Months)
            return new(false, "The property already received a leak allowance in the last 24 months.", "eligibility");
        if (daysToRepair > RepairDeadlineDays)
            return new(false, $"The repair took {daysToRepair} days. It must be completed within {RepairDeadlineDays} days of finding the leak.", "time-limits");
        if (!hasRepairInvoice)
            return new(false, "A plumber's invoice for the repair is required.", "evidence");

        return new(true, "The leak meets the location, repair time, evidence and 24-month rules.", "eligibility");
    }

    [Description("Calculates the leak allowance amount in pounds from daily water use. If eligibility has not been confirmed, present the result as an estimate.")]
    public AllowanceResult CalculateAllowance(
        [Description("Normal daily use in litres before the leak")] double normalDailyLitres,
        [Description("Daily use in litres during the leak")] double leakDailyLitres,
        [Description("Number of days the leak lasted")] int leakDays,
        [Description("'household' or 'business'")] string customerType)
    {
        int daysCounted = Math.Clamp(leakDays, 0, MaxDaysCovered);
        decimal excessLitres = (decimal)Math.Max(0, leakDailyLitres - normalDailyLitres) * daysCounted;
        decimal excessCubicMetres = Math.Round(excessLitres / 1000m, 3);
        decimal rate = WaterChargePerCubicMetre + WastewaterChargePerCubicMetre;
        decimal amount = Math.Round(excessCubicMetres * rate, 2);

        bool isBusiness = customerType.Trim().Equals("business", StringComparison.OrdinalIgnoreCase);
        decimal cap = isBusiness ? BusinessCap : HouseholdCap;
        bool capped = amount > cap;
        if (capped) amount = cap;

        // The answer may quote the result and the figures used to get it.
        TrustedAmounts.UnionWith([amount, rate, WaterChargePerCubicMetre, WastewaterChargePerCubicMetre, cap]);
        string working = $"{excessCubicMetres} m³ × £{rate} per m³ over {daysCounted} day(s)" + (capped ? $", capped at £{cap:N0}" : "");
        return new(amount, excessCubicMetres, daysCounted, capped, working);
    }
}