using Microsoft.Extensions.AI;

namespace LeakAssistant.Core;

/// <summary>Adds up tokens and cost across every model call in one question.</summary>
public sealed class UsageTracker(decimal inputPricePerMillion, decimal outputPricePerMillion)
{
    public int ModelCalls { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }

    public decimal CostUsd =>
        InputTokens / 1_000_000m * inputPricePerMillion +
        OutputTokens / 1_000_000m * outputPricePerMillion;

    public void Record(UsageDetails? usage)
    {
        ModelCalls++;
        InputTokens += usage?.InputTokenCount ?? 0;
        OutputTokens += usage?.OutputTokenCount ?? 0;
    }

    public void Reset()
    {
        ModelCalls = 0;
        InputTokens = 0;
        OutputTokens = 0;
    }

    public override string ToString() =>
        $"{ModelCalls} model call(s) · {InputTokens:N0} in · {OutputTokens:N0} out · ${CostUsd:0.00000}";
}

/// <summary>A pipeline layer that records the usage of every response passing through it.</summary>
public sealed class UsageTrackingChatClient(IChatClient innerClient, UsageTracker usage)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken);
        usage.Record(response.Usage);
        return response;
    }
}