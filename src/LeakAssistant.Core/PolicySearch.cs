using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.AI;

namespace LeakAssistant.Core;

public sealed record SearchHit(string Id, string Section, string Content, string Source, double? Score);

public sealed class PolicySearch(AzureClients clients)
{
    /// <summary>
    /// Hybrid by default: keyword (BM25) and vector results merged with Reciprocal Rank Fusion.
    /// Pass hybrid: false to see vector-only results.
    /// </summary>
    public async Task<List<SearchHit>> SearchAsync(string query, int? top = null, bool hybrid = true)
    {
        ReadOnlyMemory<float> vector = await clients.Embeddings.GenerateVectorAsync(query);

        var options = new SearchOptions
        {
            Size = top ?? clients.Settings.TopK,
            Select = { "id", "section", "source", "content" },
            VectorSearch = new()
            {
                Queries = { new VectorizedQuery(vector) { KNearestNeighborsCount = 10, Fields = { "contentVector" } } }
            }
        };

        SearchResults<PolicyChunk> results =
            await clients.Search.SearchAsync<PolicyChunk>(hybrid ? query : null, options);

        var hits = new List<SearchHit>();
        await foreach (SearchResult<PolicyChunk> result in results.GetResultsAsync())
        {
            PolicyChunk chunk = result.Document;
            hits.Add(new SearchHit(chunk.Id, chunk.Section, chunk.Content, chunk.Source, result.Score));
        }
        return hits;
    }
}