using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Standalone attach-only diagnostic. It never starts a worker, panel or CAD instance.
// Reflection is limited to the existing passive context and selection reader.
internal static class ReadNativeSelectionLayout
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
    static readonly BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static object Call(object context, string method, params object[] args)
    {
        if (!new[] { "Observation", "Windows", "ConfirmDocument", "ModelItems", "SelectedItems", "ItemData", "Dispose" }.Contains(method))
            throw new InvalidOperationException("Getter not allowed.");
        return context.GetType().GetMethod(method, Instance).Invoke(context, args);
    }
    static Dictionary<string, object> Graph(object value)
    { return Json.Deserialize<Dictionary<string, object>>(Json.Serialize(value)); }
    static string Hash(string path)
    { using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
    static object Describe(object raw)
    {
        var array = raw as Array;
        return new { isNull = raw == null, runtimeType = raw == null ? null : raw.GetType().FullName,
            rank = array == null ? (int?)null : array.Rank,
            lowerBounds = array == null ? null : Enumerable.Range(0, array.Rank).Select(array.GetLowerBound).ToArray(),
            elementTypes = array == null ? null : array.Cast<object>().Select(x => x == null ? null : x.GetType().FullName).Distinct().ToArray(), value = raw };
    }
    static void Guard(Dictionary<string, object> expected, Dictionary<string, object> fresh)
    {
        foreach (string key in new[] { "processId", "processStartedUtc", "windowsSessionId" })
            if (!Equals(expected[key], fresh[key])) throw new InvalidOperationException("Process identity changed: " + key);
        var em = Graph(expected["model"]); var fm = Graph(fresh["model"]);
        foreach (string key in new[] { "id", "name" })
            if (!Equals(em[key], fm[key])) throw new InvalidOperationException("Model identity changed: " + key);
        if (!String.Equals(Path.GetFullPath((string)em["path"]), Path.GetFullPath((string)fm["path"]), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Model path changed.");
        var contextList = expected["contextBefore"] as IList;
        if (contextList == null) throw new InvalidOperationException("Observed context must be an array.");
        var expectedContext = contextList.Cast<object>().ToArray();
        if (expectedContext.Length != 2 || !(expectedContext[0] is int) || (int)expectedContext[0] < 0 || (int)expectedContext[0] > 128 || !Equals(expectedContext[1], 0))
            throw new InvalidOperationException("Requires a bounded observed selection in world context.");
        string context = Json.Serialize(expectedContext);
        if (!Equals(fresh["busy"], false) || Json.Serialize(expected["contextAfter"]) != context || Json.Serialize(fresh["contextBefore"]) != context || Json.Serialize(fresh["contextAfter"]) != context)
            throw new InvalidOperationException("Observed selection count or world context changed.");
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 4 || args[0] != "--read-only-existing") return 2;
        string assemblyPath = Path.GetFullPath(args[1]), input = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (File.Exists(output)) return 2; // Preserve the first outcome; no overwrite/replay.
        object context = null;
        var report = new Dictionary<string, object> { { "state", "passive_layout_probe_failed" },
            { "cadCommandsAttempted", 0 }, { "selectionChangesAttempted", 0 }, { "cadSaveAttempted", false },
            { "workerStarted", false }, { "nativeLayoutQualified", false }, { "startedUtc", DateTime.UtcNow.ToString("o") } };
        int exit = 1;
        try
        {
            var expected = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(input));
            var issued = DateTimeOffset.Parse((string)expected["timestampUtc"]).UtcDateTime;
            if (DateTime.UtcNow > issued.AddSeconds(300) || issued > DateTime.UtcNow.AddSeconds(5)) throw new InvalidOperationException("Preflight expired.");
            report["assemblySha256"] = Hash(assemblyPath); report["inputSha256"] = Hash(input);
            report["diagnosticSha256"] = Hash(Assembly.GetExecutingAssembly().Location);
            var assembly = Assembly.LoadFrom(assemblyPath);
            var program = assembly.GetType("PowerShapeBridge.Program", true);
            var config = program.GetMethod("ReadConfig", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            var type = assembly.GetType("PowerShapeBridge.DocumentBindings+ComContext", true);
            context = Activator.CreateInstance(type, Instance, null, new[] { config }, null);
            var before = Graph(Call(context, "Observation")); Guard(expected, before);
            report["before"] = before; report["windowsBefore"] = Call(context, "Windows");
            string working = (string)Graph(before["model"])["path"];
            // PowerShape retains write access to its open model. These nonexclusive
            // samples observe saved bytes only; they are not an execution lease.
            {
                report["fileProtectionScope"] = "Nonexclusive before/after saved-byte hashes; no continuous write exclusion or mutation authorization.";
                string beforeHash = Hash(working); report["workingSha256Before"] = beforeHash;
                object inventory = Call(context, "ModelItems"), selected = Call(context, "SelectedItems");
                report["inventoryLayout"] = Describe(inventory); report["selectionLayout"] = Describe(selected);
                var inventoryArray = inventory as Array;
                if (inventoryArray != null && inventoryArray.Length > 0)
                {
                    var data = (object[])Call(context, "ItemData", inventory);
                    report["itemDataLayouts"] = data.Select(Describe).ToArray();
                }
                var reader = assembly.GetType("PowerShapeBridge.NativeSelectionReading", true);
                var result = Graph(reader.GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { before, DateTime.UtcNow.AddSeconds(60), context }));
                report["strictReader"] = result;
                var after = Graph(Call(context, "Observation")); Guard(expected, after);
                report["after"] = after; report["windowsAfter"] = Call(context, "Windows");
                report["workingSha256After"] = Hash(working);
                if (beforeHash != (string)report["workingSha256After"] || Json.Serialize(report["windowsBefore"]) != Json.Serialize(report["windowsAfter"]))
                    throw new InvalidOperationException("Saved bytes or window context changed.");
                Call(context, "ConfirmDocument");
                report["state"] = Equals(result["state"], "native_selection_items_read_unqualified") ? "passive_selection_layout_observed" : "passive_layout_observed_reader_rejected";
                report["scope"] = "Existing document and independently observed current selection only; no selection change or DGK export. Native layout qualification remains separate.";
                exit = 0;
            }
        }
        catch (Exception error) { report["error"] = (error.InnerException ?? error).ToString(); }
        finally
        {
            if (context != null) try { Call(context, "Dispose"); } catch (Exception error) { report["disposeError"] = error.Message; exit = 1; }
            report["finishedUtc"] = DateTime.UtcNow.ToString("o");
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) using (var writer = new StreamWriter(file)) writer.Write(Json.Serialize(report));
        }
        Console.WriteLine(Json.Serialize(new { state = report["state"], reportPath = output }));
        return exit;
    }
}
