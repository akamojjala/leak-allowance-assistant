using System.Text;
using System.Text.Json;
using LeakAssistant.Core;
using LeakAssistant.Eval;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

// Run from the solution folder:  dotnet run --project src/LeakAssistant.Eval -- eval/questions.json
var settings = LabSettings.Load();
var clients = new AzureClients(settings);

string casesPath = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("eval", "questions.json"));
List<EvalCase> cases = JsonSerializer.Deserialize<List<EvalCase>>(
        await File.ReadAllTextAsync(casesPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("No questions found.");

var usage = new UsageTracker(settings.InputPricePerMillion, settings.OutputPricePerMillion);
var tools = new LeakTools(new PolicySearch(clients));
AIAgent agent = AssistantFactory.Create(clients.CreateChatClient(usage), tools);

// The judge has its own client, so its tokens are not counted as the assistant's cost.
// GroundednessEvaluator asks for temperature 0 and at most 800 output tokens, settings tuned for GPT-4o.
// GPT-5 models reject a temperature setting and spend output tokens on hidden reasoning,
// so this layer removes those settings before every judge call.
IChatClient judgeClient = clients.OpenAI.GetChatClient(settings.JudgeDeployment)
    .AsIChatClient()
    .AsBuilder()
    .ConfigureOptions(options =>
    {
        options.Temperature = null;
        options.TopP = null;
        options.PresencePenalty = null;
        options.FrequencyPenalty = null;
        options.MaxOutputTokens = null;
    })
    .Build();
var judge = new ChatConfiguration(judgeClient);
var groundedness = new GroundednessEvaluator();

Console.WriteLine($"Evaluating {cases.Count} questions with {settings.ChatDeployment} (top {settings.TopK}), judged by {settings.JudgeDeployment}\n");
var rows = new List<EvalRow>();

foreach (EvalCase c in cases)
{
    usage.Reset();
    tools.ClearConversation();

    string answer;
    try
    {
        AgentSession session = await agent.CreateSessionAsync();
        answer = (await agent.RunAsync(c.Question, session)).ToString();
        GuardResult guard = OutputGuard.Check(answer, tools);
        if (!guard.Allowed)
        {
            Console.WriteLine($"      {guard.Reason}");
            answer = "[blocked by the output guard]";
        }
    }
    catch (Exception ex) when (ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
                            || ex.Message.Contains("content management policy", StringComparison.OrdinalIgnoreCase))
    {
        answer = "[blocked by the Azure OpenAI content filter]";
    }

    bool passed = LeakAssistant.Eval.EvalChecks.Passed(c, answer);
    bool cited = tools.RetrievedThisTurn.Any(hit => answer.Contains($"[{hit.Id}]"));

    double? grounded = null;
    if (c.Type is "fact" or "tool" && tools.RetrievedThisTurn.Count > 0)
    {
        string context = string.Join("\n\n", tools.RetrievedThisTurn.Select(hit => hit.Content).Distinct());
        EvaluationResult result = await groundedness.EvaluateAsync(
            [new ChatMessage(ChatRole.User, c.Question)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)),
            judge,
            [new GroundednessEvaluatorContext(context)]);
        grounded = result.Get<NumericMetric>(GroundednessEvaluator.GroundednessMetricName).Value;
    }

    rows.Add(new EvalRow(c.Id, c.Type, passed, grounded, cited, usage.ModelCalls, usage.InputTokens + usage.OutputTokens, usage.CostUsd));
    string groundedText = grounded.HasValue ? grounded.Value.ToString("0.0") : " - ";
    Console.WriteLine($"{c.Id,-4} {c.Type,-9} {(passed ? "PASS" : "FAIL"),-4}  grounded {groundedText,3}  cited {(cited ? "yes" : "no "),-3}  {usage}");
    if (!passed) Console.WriteLine($"      answer: {answer}");
}

// Summary table, printed and saved as Markdown for the README.
var md = new StringBuilder();
md.AppendLine($"### {settings.ChatDeployment}, top {settings.TopK} ({DateTime.Now:yyyy-MM-dd HH:mm})");
md.AppendLine();
md.AppendLine("| Question type | Passed | Avg groundedness (1–5) | Cited a source | Avg cost per answer (USD) |");
md.AppendLine("|---|---|---|---|---|");
foreach (IGrouping<string, EvalRow> group in rows.GroupBy(row => row.Type))
{
    md.AppendLine($"| {group.Key} | {group.Count(r => r.Passed)}/{group.Count()} | {AverageGrounded(group)} | {group.Count(r => r.Cited)}/{group.Count()} | {group.Average(r => r.CostUsd):0.00000} |");
}
md.AppendLine($"| **all** | **{rows.Count(r => r.Passed)}/{rows.Count}** | **{AverageGrounded(rows)}** | {rows.Count(r => r.Cited)}/{rows.Count} | **{rows.Average(r => r.CostUsd):0.00000}** |");

Console.WriteLine();
Console.WriteLine(md);

string outFile = Path.Combine(
    Path.GetDirectoryName(casesPath)!,
    $"results-{settings.ChatDeployment}-top{settings.TopK}-{DateTime.Now:yyyyMMdd-HHmm}.md");
await File.WriteAllTextAsync(outFile, md.ToString());
Console.WriteLine($"Saved {outFile}");

static string AverageGrounded(IEnumerable<EvalRow> rows)
{
    List<double> scores = rows.Where(r => r.Groundedness.HasValue).Select(r => r.Groundedness!.Value).ToList();
    return scores.Count == 0 ? "–" : scores.Average().ToString("0.0");
}