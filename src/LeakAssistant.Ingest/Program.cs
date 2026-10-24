using LeakAssistant.Core;

// Run from the solution folder:  dotnet run --project src/LeakAssistant.Ingest -- docs
var settings = LabSettings.Load();
var clients = new AzureClients(settings);

string docsFolder = Path.GetFullPath(args.Length > 0 ? args[0] : "docs");
if (!Directory.Exists(docsFolder))
{
    Console.WriteLine($"Folder not found: {docsFolder}");
    Console.WriteLine("Run from the solution folder: dotnet run --project src/LeakAssistant.Ingest -- docs");
    return;
}

Console.WriteLine($"Creating or updating index '{settings.IndexName}'...");
await PolicyIndex.CreateOrUpdateAsync(clients);

var chunks = new List<PolicyChunk>();
foreach (string file in Directory.GetFiles(docsFolder, "*.md").Order())
{
    List<PolicyChunk> fileChunks = PolicyIndex.ChunkMarkdown(file, await File.ReadAllTextAsync(file));
    Console.WriteLine($"  {Path.GetFileName(file),-24} {fileChunks.Count} chunk(s)  [{fileChunks.FirstOrDefault()?.Source}]");
    chunks.AddRange(fileChunks);
}

Console.WriteLine($"Embedding and uploading {chunks.Count} chunks...");
int uploaded = await PolicyIndex.UploadAsync(clients, chunks);
Console.WriteLine($"Done: {uploaded} of {chunks.Count} chunks are in the index.");