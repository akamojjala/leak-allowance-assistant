using System.Text.Json.Serialization;

namespace LeakAssistant.Core;

/// <summary>One searchable section of a policy document. Field names match the index.</summary>
public sealed class PolicyChunk
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("document")] public string Document { get; set; } = "";
    [JsonPropertyName("section")] public string Section { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "official";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("contentVector")] public float[]? ContentVector { get; set; }
}