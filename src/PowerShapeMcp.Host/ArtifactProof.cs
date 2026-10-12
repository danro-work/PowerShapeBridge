using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public static class ArtifactProof
{
    public static JsonObject Verified(string root,string folder,string id,string sha,int limit)
    {
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("operation_artifact_identity_invalid");
        var raw=FileArtifacts.ReadBytes(root,Path.Combine(folder,id+".json"),limit);
        if(FileArtifacts.Sha256(raw)!=sha)throw new ReferenceRejected("operation_artifact_changed");
        return JsonNode.Parse(FileArtifacts.Parse(raw).GetRawText())!.AsObject();
    }
    public static string Freeze(string root,string relative,JsonObject value)
    {
        var path=FileArtifacts.FixedPath(root,relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var raw=new UTF8Encoding(false,true).GetBytes(PythonJson.Indented(JsonSerializer.SerializeToElement(value)));
        using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {stream.Write(raw);stream.Flush(true);}
        return FileArtifacts.Sha256(raw);
    }
    public static bool Fields(JsonObject value,params string[] names)=>new HashSet<string>(value.Select(p=>p.Key)).SetEquals(names);
    public static bool Boolean(JsonNode? value,bool expected)=>value is JsonValue scalar&&scalar.TryGetValue<bool>(out var actual)&&actual==expected;
    public static string String(JsonObject value,string name,string code)=>value[name] is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text:throw new ReferenceRejected(code);
    public static JsonObject Journal(string root,string id)
    {
        var result=new OperationJournal(root).Read(id);
        if(result["state"]?.GetValue<string>()!="operation_receipt_read"||result["receipt"] is not JsonObject journal)
            throw new ReferenceRejected("operation_journal_unreadable");
        return journal;
    }
}
