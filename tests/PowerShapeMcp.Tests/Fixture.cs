using System.Text.Json.Nodes;
using PowerShapeMcp;

namespace PowerShapeMcp.Tests;

public sealed class Fixture
{
    public string Root { get; } = Path.Combine(Directory.GetCurrentDirectory(), "work", "K2-test-" + Guid.NewGuid().ToString("N"));
    public JsonObject Config { get; } = new()
    {
        ["coreProfile"] = "general-v1", ["expectedVersion"] = "23.2.2",
        ["timeoutSeconds"] = 20, ["operationTimeoutSeconds"] = 80, ["maxEntities"] = 10000,
        ["captureViewport"] = false
    };
    public Fixture()
    {
        Directory.CreateDirectory(Root);
        SaveConfig();
    }
    public void SaveConfig() => Write("bridge.config.json", Config.ToJsonString());
    public void Write(string relative, string content)
    {
        var path = FileArtifacts.FixedPath(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".test-tmp", content, new System.Text.UTF8Encoding(false));
        File.Move(path + ".test-tmp", path, true);
    }
    public HostOptions Options(bool readOnly = false, double seconds = 1) => new(Root, TimeSpan.FromSeconds(seconds), readOnly);
    public async Task<string> WaitRequest()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var folder = Path.Combine(Root, "requests");
            var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
            if (files.Length != 0) return files.Single();
            await Task.Delay(10);
        }
        throw new TimeoutException("Fake panel did not receive a request.");
    }
    public async Task<string> ClaimRequest()
    {
        var request=await WaitRequest();
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(true)
        {
            try{File.Move(request,request+".processing");return request;}
            catch(IOException)when(DateTime.UtcNow<deadline){await Task.Delay(10);}
        }
    }
}
