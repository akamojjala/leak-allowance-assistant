using LeakAssistant.Core;
using Microsoft.Agents.AI;

var settings = LabSettings.Load();
var clients = new AzureClients(settings);
var usage = new UsageTracker(settings.InputPricePerMillion, settings.OutputPricePerMillion);
var tools = new LeakTools(new PolicySearch(clients));
AIAgent agent = AssistantFactory.Create(clients.CreateChatClient(usage), tools);
AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine($"Contoso Water leak allowance assistant ({settings.ChatDeployment}). Ask a question, or type exit.");
while (true)
{
    Console.Write("\nYou > ");
    string? question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question) || question.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

    usage.Reset();
    tools.ClearTurn();

    string answer;
    try
    {
        answer = (await agent.RunAsync(question, session)).ToString();
    }
    catch (Exception ex)
    {
        // A content-filter rejection or a service error: show it instead of crashing.
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