using System.Text;
using Azure.Search.Documents.Indexes.Models;
using Microsoft.Extensions.AI;

namespace LeakAssistant.Core;

public static class PolicyIndex
{
    /// <summary>text-embedding-3-small returns 1,536 numbers per text.</summary>
    public const int VectorDimensions = 1536;

    public static async Task CreateOrUpdateAsync(AzureClients clients)
    {
        var vectorSearch = new VectorSearch();
        vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(name: "hnsw-default"));
        vectorSearch.Profiles.Add(new VectorSearchProfile(name: "vector-profile", algorithmConfigurationName: "hnsw-default"));

        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SimpleField("document", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SearchableField("section") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
            new SimpleField("source", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SearchableField("content") { AnalyzerName = LexicalAnalyzerName.EnMicrosoft },
            new VectorSearchField("contentVector", VectorDimensions, "vector-profile")
        };

        var index = new SearchIndex(clients.Settings.IndexName)
        {
            Fields = fields,
            VectorSearch = vectorSearch
        };

        await clients.SearchIndexes.CreateOrUpdateIndexAsync(index);
    }

    /// <summary>
    /// One chunk per "## " section, with the page title kept on every chunk so it makes sense alone.
    /// Files whose name starts with "community-" are marked as untrusted.
    /// </summary>
    public static List<PolicyChunk> ChunkMarkdown(string filePath, string markdown)
    {
        string document = Path.GetFileNameWithoutExtension(filePath);
        string source = document.StartsWith("community-", StringComparison.OrdinalIgnoreCase) ? "community" : "official";
        string title = document;
        string? section = null;
        var body = new StringBuilder();
        var chunks = new List<PolicyChunk>();

        void Flush()
        {
            string text = body.ToString().Trim();
            body.Clear();
            if (section is null || text.Length == 0) return;

            chunks.Add(new PolicyChunk
            {
                Id = $"{document}-{chunks.Count + 1}",
                Document = document,
                Section = section,
                Source = source,
                Content = $"{title}: {section}\n{text}"
            });
        }

        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.StartsWith("# ")) { title = line[2..].Trim(); continue; }
            if (line.StartsWith("## ")) { Flush(); section = line[3..].Trim(); continue; }
            section ??= "Introduction";
            body.AppendLine(line);
        }
        Flush();
        return chunks;
    }

    /// <summary>Embeds each chunk and uploads it. MergeOrUpload plus stable ids makes re-runs safe.</summary>
    public static async Task<int> UploadAsync(AzureClients clients, List<PolicyChunk> chunks)
    {
        foreach (PolicyChunk chunk in chunks)
        {
            ReadOnlyMemory<float> vector = await clients.Embeddings.GenerateVectorAsync(chunk.Content);
            chunk.ContentVector = vector.ToArray();
        }

        var response = await clients.Search.MergeOrUploadDocumentsAsync(chunks);
        return response.Value.Results.Count(result => result.Succeeded);
    }
}