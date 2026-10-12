using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Only the first passive event is written by the SDK host. Native executor/verifier
// retains all subsequent command transitions and interrupted-writer ownership.
public static class PreparedJournal
{
    public static string Create(string root,string id,string sha,DateTimeOffset timestamp,JsonObject details)
    {
        if(!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("operation_journal_append_invalid");
        var folder=FileArtifacts.FixedPath(root,"operations",id);Directory.CreateDirectory(folder);
        var marker=FileArtifacts.FixedPath(root,"operations",id,"writer.lock");
        string eventSha;
        using(var writer=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            writer.Write("locked"u8);writer.Flush(true);
            if(Directory.GetFiles(folder,"*.json").Length!=0)throw new ReferenceRejected("operation_journal_concurrent_or_repeated_append");
            var entry=new JsonObject{["schemaVersion"]=1,["operationId"]=id,["planSha256"]=sha,["sequence"]=0,["previousSha256"]=null,
                ["state"]="prepared",["timestampUtc"]=LeaseTime.Format(timestamp),["details"]=details.DeepClone()};
            eventSha=ArtifactProof.Freeze(root,Path.Combine("operations",id,"000000.json"),entry);
        }
        // Remove only the successfully completed writer marker owned by this call.
        // Any failure above leaves it for explicit inspection; no automatic repair.
        File.Delete(marker);return eventSha;
    }
}
