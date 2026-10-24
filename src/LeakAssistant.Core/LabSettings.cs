using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace LeakAssistant.Core;

/// <summary>
/// Every setting the lab uses. Read from user secrets (id "leak-assistant-lab"), then from
/// environment variables that start with LAB_, which win. Example: LAB_AzureOpenAI__ChatDeployment.
/// </summary>
public sealed record LabSettings(
    string OpenAIEndpoint,
    string ChatDeployment,
    string JudgeDeployment,
    string EmbeddingDeployment,
    string SearchEndpoint,
    string IndexName,
    int TopK,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion)
{
    public const string SecretsId = "leak-assistant-lab";

    public static LabSettings Load()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddUserSecrets(SecretsId)
            .AddEnvironmentVariables(prefix: "LAB_")
            .Build();

        string Required(string key) =>
            config[key] ?? throw new InvalidOperationException(
                $"Missing setting '{key}'. Set it with: dotnet user-secrets set \"{key}\" \"<value>\" --id {SecretsId}");

        string chatDeployment = Required("AzureOpenAI:ChatDeployment");

        return new LabSettings(
            OpenAIEndpoint: Required("AzureOpenAI:Endpoint"),
            ChatDeployment: chatDeployment,
            JudgeDeployment: config["AzureOpenAI:JudgeDeployment"] ?? chatDeployment,
            EmbeddingDeployment: Required("AzureOpenAI:EmbeddingDeployment"),
            SearchEndpoint: Required("Search:Endpoint"),
            IndexName: config["Search:IndexName"] ?? "leak-policies",
            TopK: int.Parse(config["Search:TopK"] ?? "3", CultureInfo.InvariantCulture),
            InputPricePerMillion: decimal.Parse(config["Pricing:InputPerMillion"] ?? "0.25", CultureInfo.InvariantCulture),
            OutputPricePerMillion: decimal.Parse(config["Pricing:OutputPerMillion"] ?? "2.00", CultureInfo.InvariantCulture));
    }
}