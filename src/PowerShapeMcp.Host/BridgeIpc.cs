using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Fixed IPC seam, deliberately separate from SDK and native COM. Public tools use fixed recipe mappings.
public sealed class BridgeIpc(HostOptions options) : IDisposable
{
    private readonly SemaphoreSlim submission = new(1, 1);
    private int pending;
    private static readonly HashSet<string> Operations = ["model.snapshot", "disk.verify", "baseline.activate", "translation.prepare", "translation.execute", "recipe.prepare", "recipe.execute"];

    public async Task<JsonObject> SubmitAsync(string operation, JsonObject? arguments, CancellationToken cancellationToken = default, TimeSpan? remainingBudget = null)
    {
        if (!Operations.Contains(operation)) throw new ArgumentException("Unknown fixed queue operation.");
        if (Interlocked.Increment(ref pending) > 8)
        {
            Interlocked.Decrement(ref pending);
            return State("too_many_pending_calls");
        }
        var timer = Stopwatch.StartNew();
        var timeout=remainingBudget is { } budget&&budget<options.Timeout?budget:options.Timeout;
        var acquired = false;
        string? request = null, temporary = null, token = null;
        try
        {
            while (!(acquired = await submission.WaitAsync(TimeSpan.FromMilliseconds(25), CancellationToken.None)))
            {
                if (cancellationToken.IsCancellationRequested) return State("cancelled_before_submission");
                if (timer.Elapsed >= timeout) return State("queue_timeout_before_submission");
            }
            if (cancellationToken.IsCancellationRequested) return State("cancelled_before_submission");
            if (timer.Elapsed >= timeout) return State("queue_timeout_before_submission");
            if (Maintenance()) return State("maintenance_before_submission");
            if (!File.Exists(FileArtifacts.FixedPath(options.BridgeRoot, "PSBridge.Worker.exe"))) return State("failed", error: "Bridge worker is missing at the configured root.");
            Directory.CreateDirectory(FileArtifacts.FixedPath(options.BridgeRoot, "requests"));
            token = Guid.NewGuid().ToString("N");
            request = FileArtifacts.FixedPath(options.BridgeRoot, "requests", token + ".json");
            temporary = FileArtifacts.FixedPath(options.BridgeRoot, "requests", token + ".tmp");
            var response = FileArtifacts.FixedPath(options.BridgeRoot, "responses", token + ".json");
            var envelope = new JsonObject { ["requestId"] = token, ["operation"] = operation };
            if (arguments is not null) envelope["arguments"] = arguments.DeepClone();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                stream.Write(bytes); stream.Flush(true);
            }
            if (cancellationToken.IsCancellationRequested) return State("cancelled_before_submission");
            if (Maintenance()) return State("maintenance_before_submission");
            if (timer.Elapsed >= timeout) return State("queue_timeout_before_submission");
            File.Move(temporary, request);
            temporary = null;
            while (timer.Elapsed < timeout)
            {
                if (cancellationToken.IsCancellationRequested) return State("cancelled_outcome_unknown", token, "Cancellation does not stop a claimed PowerShape operation.");
                if (File.Exists(response))
                {
                    var data = FileArtifacts.ReadJson(options.BridgeRoot, Path.Combine("responses", token + ".json"), 16000000);
                    if (!data.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String ||
                        data.TryGetProperty("bridgeRequestId", out var identity) && identity.GetString() != token)
                        throw new InvalidDataException("Invalid Bridge response identity/state.");
                    var result = JsonNode.Parse(data.GetRawText())!.AsObject();
                    result["bridgeRequestId"] = token;
                    return result;
                }
                await Task.Delay(25, CancellationToken.None);
            }
            return State("timeout_outcome_unknown", token, "No response before deadline. No retry performed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            return State(token is not null && temporary is null ? "ipc_failure_outcome_unknown" : "failed_before_submission", token, ex.Message);
        }
        finally
        {
            // Only own unclaimed .json/.tmp. Claimed .processing and late receipts survive.
            foreach (var path in new[] { request, temporary })
                if (path is not null) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            if (acquired) submission.Release();
            Interlocked.Decrement(ref pending);
        }
    }
    private bool Maintenance() => File.Exists(FileArtifacts.FixedPath(options.BridgeRoot, "maintenance.json"));
    private static JsonObject State(string state, string? token = null, string? error = null)
    {
        var data = new JsonObject { ["state"] = state };
        if (token is not null) data["bridgeRequestId"] = token;
        if (error is not null) data["error"] = error;
        return data;
    }
    public void Dispose() => submission.Dispose();
}
