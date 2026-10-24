using LeakAssistant.Core;
using Microsoft.Agents.AI;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Set $env:LAB_TRACE = "1" to print OpenTelemetry spans and metrics to the console.
bool trace = Environment.GetEnvironmentVariable("LAB_TRACE") == "1";
ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService("LeakAssistant.Chat");

using TracerProvider? tracerProvider = trace
    ? Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(resource)
        .AddSource(Telemetry.SourceName)
        .AddConsoleExporter()
        .Build()
    : null;

using MeterProvider? meterProvider = trace
    ? Sdk.CreateMeterProviderBuilder()
        .SetResourceBuilder(resource)
        .AddMeter(Telemetry.SourceName)
        .AddConsoleExporter()
        .Build()
    : null;

var settings = LabSettings.Load();
var clients = new AzureClients(settings);
var usage = new UsageTracker(settings.InputPricePerMillion, settings.OutputPricePerMillion);
var tools = new LeakTools(new PolicySearch(clients));
AIAgent agent = AssistantFactory.Create(clients.CreateChatClient(usage), tools);
AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine($"Contoso Water leak allowance assistant ({settings.ChatDeployment}, tracing {(trace ? "on" : "off")}). Ask a question, or type exit.");
while (true)
{
    Console.Write("\nYou > ");
    string? question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question) || question.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    // Module 10: "/form <path>" sends the text of a scanned claim form with a fixed question.
    if (question.StartsWith("/form ", StringComparison.OrdinalIgnoreCase))
    {
        string formPath = question[6..].Trim().Trim('"');
        if (!File.Exists(formPath)) { Console.WriteLine($"  File not found: {formPath}"); continue; }
        question = $"Here is my leak allowance claim form:\n\n{await File.ReadAllTextAsync(formPath)}\n\nAm I eligible, and how much would I get?";
    }

    usage.Reset();
    tools.ClearTurn();

    string answer;
    try
    {
        answer = (await agent.RunAsync(question, session)).ToString();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  [error] {ex.Message}");
        Console.ResetColor();
        continue;
    }

    GuardResult guard = OutputGuard.Check(answer, tools);
    if (!guard.Allowed)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n  [guard] {guard.Reason}");
        Console.ResetColor();
        answer = "Sorry, I can't give you that answer. Please contact the customer team.";
    }

    Console.WriteLine($"\nAssistant > {answer}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  sources: {string.Join(", ", tools.RetrievedThisTurn.Select(hit => $"{hit.Id}({hit.Source})").Distinct())}");
    Console.WriteLine($"  usage:   {usage}");
    Console.ResetColor();
}