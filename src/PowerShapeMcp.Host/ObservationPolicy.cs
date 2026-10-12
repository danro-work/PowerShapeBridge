using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class ReferenceRejected(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public readonly record struct EntityKey(long Type, long Id);
public sealed record Observation(JsonObject Identity, SortedDictionary<EntityKey, JsonElement> Entities, string Digest);

public static class ObservationPolicy
{
    public static Observation Read(JsonObject snapshot)
    {
        var data = JsonSerializer.SerializeToElement(snapshot);
        if (String(data, "state", "fresh_inspection_failed") != "succeeded_verified") throw new ReferenceRejected("fresh_inspection_failed");
        if (!data.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object) throw new ReferenceRejected("document_identity_unavailable");
        var process = Integer(data,"processId","document_identity_unavailable");
        var session = Integer(data,"windowsSessionId","document_identity_unavailable");
        var modelId = Integer(model,"id","document_identity_unavailable");
        if (process < 0 || session < 0 || modelId < 0) throw new ReferenceRejected("document_identity_unavailable");
        var started = String(data,"processStartedUtc","process_identity_unavailable");
        if (started.Length == 0) throw new ReferenceRejected("process_identity_unavailable");
        var identity = new JsonObject
        {
            ["processId"] = process, ["processStartedUtc"] = started, ["windowsSessionId"] = session,
            ["modelId"] = modelId, ["modelName"] = String(model,"name","document_identity_unavailable"),
            ["modelPath"] = NormalizePath(String(model,"path","document_identity_unavailable"))
        };
        var count = Integer(data,"entityCount","incomplete_inventory");
        if (!data.TryGetProperty("entities",out var inventory) || inventory.ValueKind != JsonValueKind.Array || inventory.GetArrayLength() != count)
            throw new ReferenceRejected("incomplete_inventory");
        var entities = new SortedDictionary<EntityKey,JsonElement>(Comparer<EntityKey>.Create((a,b) => a.Type != b.Type ? a.Type.CompareTo(b.Type) : a.Id.CompareTo(b.Id)));
        foreach (var entity in inventory.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object) throw new ReferenceRejected("incomplete_inventory");
            var type = Integer(entity,"nativeTypeCode","ambiguous_entity_identity");
            var id = Integer(entity,"nativeId","ambiguous_entity_identity");
            if (type < 0 || id < 0 || !entities.TryAdd(new(type,id),entity.Clone())) throw new ReferenceRejected("ambiguous_entity_identity");
            _ = String(entity,"type","incomplete_inventory"); _ = String(entity,"name","incomplete_inventory");
            _ = Integer(entity,"levelNumber","incomplete_inventory");
        }
        JsonNode? workplane = null;
        if (data.TryGetProperty("contextBefore",out var context))
        {
            if (context.ValueKind != JsonValueKind.Array || context.GetArrayLength() < 2) throw new ReferenceRejected("observed_context_invalid");
            workplane = JsonNode.Parse(context[1].GetRawText());
        }
        var units = new JsonObject();
        foreach (var field in new[] {"lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})
            units[field] = data.TryGetProperty(field,out var value) ? JsonNode.Parse(value.GetRawText()) : null;
        var observed = new JsonObject
        {
            ["entities"] = new JsonArray(entities.Values.Select(e => JsonNode.Parse(e.GetRawText())).ToArray()),
            ["contextWorkplane"] = workplane, ["units"] = units,
            ["workplanes"] = data.TryGetProperty("workplanes",out var planes) ? JsonNode.Parse(planes.GetRawText()) : null
        };
        return new(identity,entities,Digest(observed));
    }
    public static string String(JsonElement value, string field, string error) =>
        value.TryGetProperty(field,out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : throw new ReferenceRejected(error);
    public static long Integer(JsonElement value, string field, string error) =>
        value.TryGetProperty(field,out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number) ? number : throw new ReferenceRejected(error);

    public static string Digest(JsonNode value) => FileArtifacts.Sha256(new UTF8Encoding(false,true).GetBytes(Canonical(JsonSerializer.SerializeToElement(value))));
    public static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",",value.EnumerateObject().OrderBy(p=>p.Name,UnicodeComparer.Instance).Select(p=>PythonJson.Quote(p.Name)+":"+Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",",value.EnumerateArray().Select(Canonical)) + "]",
        _ => PythonJson.Dump(value)
    };
    private sealed class UnicodeComparer : IComparer<string>
    {
        public static UnicodeComparer Instance { get; } = new();
        public int Compare(string? x,string? y)
        {
            if (x is null || y is null) return StringComparer.Ordinal.Compare(x,y);
            var a=x.EnumerateRunes().GetEnumerator(); var b=y.EnumerateRunes().GetEnumerator();
            while (true)
            {
                var hasA=a.MoveNext(); var hasB=b.MoveNext();
                if (!hasA || !hasB) return hasA.CompareTo(hasB);
                var order=a.Current.Value.CompareTo(b.Current.Value); if(order!=0)return order;
            }
        }
    }

    // ntpath.normcase(normpath), without resolving relative/unsaved paths on disk.
    public static string NormalizePath(string path)
    {
        path=path.Replace('/','\\'); var drive=""; var root=""; var tail=path;
        if (path.StartsWith("\\\\",StringComparison.Ordinal))
        {
            var start=path.StartsWith("\\\\?\\UNC\\",StringComparison.OrdinalIgnoreCase)?8:2;
            var serverEnd=path.IndexOf('\\',start); var shareEnd=serverEnd<0?-1:path.IndexOf('\\',serverEnd+1);
            if(shareEnd<0){drive=path;tail="";}else{drive=path[..shareEnd];root="\\";tail=path[(shareEnd+1)..];}
        }
        else if(path.Length>=2&&path[1]==':'){drive=path[..2];tail=path[2..];if(tail.StartsWith('\\')){root="\\";tail=tail[1..];}}
        else if(path.StartsWith('\\')){root="\\";tail=path[1..];}
        var parts=new List<string>();
        foreach(var part in tail.Split('\\'))
        {
            if(part is "" or ".")continue;
            if(part==".."&&parts.Count>0&&parts[^1]!=".."){parts.RemoveAt(parts.Count-1);continue;}
            if(part==".."&&parts.Count==0&&root.Length>0)continue;
            parts.Add(part);
        }
        if(drive.Length==0&&root.Length==0&&parts.Count==0)parts.Add(".");
        return (drive+root+string.Join("\\",parts)).ToLowerInvariant();
    }
}
