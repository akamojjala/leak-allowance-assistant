using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.AI;

namespace LeakAssistant.Core;

/// <summary>Creates every Azure client the lab needs, signed in as you through Entra ID. No keys.</summary>
public sealed class AzureClients
{
    public AzureClients(LabSettings settings)
    {
        Settings = settings;
        var credential = new DefaultAzureCredential();

        OpenAI = new AzureOpenAIClient(new Uri(settings.OpenAIEndpoint), credential);
        Embeddings = OpenAI.GetEmbeddingClient(settings.EmbeddingDeployment).AsIEmbeddingGenerator();
        SearchIndexes = new SearchIndexClient(new Uri(settings.SearchEndpoint), credential);
        Search = SearchIndexes.GetSearchClient(settings.IndexName);
    }

    public LabSettings Settings { get; }
    public AzureOpenAIClient OpenAI { get; }
    public IEmbeddingGenerator<string, Embedding<float>> Embeddings { get; }
    public SearchIndexClient SearchIndexes { get; }
    public SearchClient Search { get; }

    /// <summary>
    /// The chat pipeline, outermost layer first:
    /// tool-call loop (at most 5 rounds) → token and cost counter → OpenTelemetry → Azure OpenAI.
    /// </summary>
    public IChatClient CreateChatClient(UsageTracker usage) =>
        OpenAI.GetChatClient(Settings.ChatDeployment)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(configure: loop => loop.MaximumIterationsPerRequest = 5)
            .Use(inner => new UsageTrackingChatClient(inner, usage))
            .UseOpenTelemetry(sourceName: Telemetry.SourceName)
            .Build();
}