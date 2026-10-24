using System.Globalization;
using System.Text.RegularExpressions;

namespace LeakAssistant.Core;

public static partial class Money
{
    [GeneratedRegex(@"£\s?\d[\d,]*(?:\.\d{1,2})?")]
    private static partial Regex Pattern();

    /// <summary>Every £ amount in the text, as numbers. "£3,000" becomes 3000 and "£4.50" becomes 4.50.</summary>
    public static List<decimal> FindAmounts(string text) =>
        Pattern().Matches(text)
            .Select(match => decimal.Parse(match.Value.Replace("£", "").Replace(",", "").Trim(), CultureInfo.InvariantCulture))
            .ToList();
}