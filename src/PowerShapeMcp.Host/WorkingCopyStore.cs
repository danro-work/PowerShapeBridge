using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PowerShapeMcp;

public sealed class WorkingCopyStore(Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,JsonObject Parameters);
    private readonly Dictionary<string,Lease> copies=[];
    public void Clear()=>copies.Clear();
    public void Revoke(string handle)=>copies.Remove(handle);
    private void Purge(){var time=now();foreach(var id in copies.Where(p=>p.Value.Expires<=time).Select(p=>p.Key).ToArray())copies.Remove(id);}
    public static void ValidateSource(JsonObject args)
    {
        if(!ArtifactProof.Fields(args,"sourcePath","sourceSha256"))throw new ReferenceRejected("working_copy_source_arguments_invalid");
        var path=ArtifactProof.String(args,"sourcePath","working_copy_saved_source_invalid");
        var sha=ArtifactProof.String(args,"sourceSha256","working_copy_saved_source_invalid");
        if(!Regex.IsMatch(path,@"^[A-Za-z]:[\\/]")||!path.EndsWith(".psmodel",StringComparison.OrdinalIgnoreCase)||!FileArtifacts.Token(sha,64))throw new ReferenceRejected("working_copy_saved_source_invalid");
        if(path.Any(c=>"\0*?<>\"|".Contains(c))||path[2..].Contains(':'))throw new ReferenceRejected("working_copy_source_path_invalid");
        if(Regex.Split(path[3..],@"[\\/]").Any(part=>part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')))throw new ReferenceRejected("working_copy_source_path_ambiguous");
    }
    public JsonObject Issue(JsonObject result)
    {
        var id=ArtifactProof.String(result,"copyId","working_copy_native_receipt_invalid");var sha=ArtifactProof.String(result,"ownershipSha256","working_copy_native_receipt_invalid");
        if(result["state"]?.GetValue<string>()!="working_copy_prepared_disk"||!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64)||result["bridgeRequestId"]?.GetValue<string>()!=id)throw new ReferenceRejected("working_copy_native_receipt_invalid");
        var time=LeaseTime.Remaining(ArtifactProof.String(result,"timestampUtc","entity_set_time_invalid_or_expired"),utcNow(),"entity_set_time_invalid_or_expired");
        return IssueLease(result,Math.Min(time.Remaining,300));
    }
    public JsonObject IssueVerifiedOpen(JsonObject result)
    {
        var id=ArtifactProof.String(result,"copyId","verified_open_native_receipt_invalid");var sha=ArtifactProof.String(result,"ownershipSha256","verified_open_native_receipt_invalid");
        if(result["state"]?.GetValue<string>()!="owned_open_verified_observed"||!FileArtifacts.Token(id)||!FileArtifacts.Token(sha,64)||
           !FileArtifacts.Token(result["openOperationId"]?.GetValue<string>())||!FileArtifacts.Token(result["bridgeRequestId"]?.GetValue<string>())||result["recordedJournalState"]?.GetValue<string>()!="succeeded_verified")throw new ReferenceRejected("verified_open_native_receipt_invalid");
        var time=LeaseTime.Remaining(ArtifactProof.String(result,"timestampUtc","verified_open_expired"),utcNow(),"verified_open_expired");
        var expiry=LeaseTime.Parse(ArtifactProof.String(result,"expiresUtc","verified_open_expired"),"verified_open_expired");
        var seconds=Math.Min(Math.Min(time.Remaining,300),(expiry-LeaseTime.PythonPrecision(utcNow())).TotalSeconds);
        if(seconds<=0)throw new ReferenceRejected("verified_open_expired");return IssueLease(result,seconds);
    }
    // Called only after the operation-specific native proof and fresh independent
    // verification have passed. This is current owned-copy access, not an OPEN
    // claim, execution authority, or evidence that unsaved changes are on disk.
    internal JsonObject IssueVerifiedWorkplane(JsonObject result,JsonObject ownedCopy,DateTimeOffset expiry)
    {
        const string code="verified_workplane_copy_lease_invalid";
        if(!ArtifactProof.Fields(ownedCopy,"copyId","ownershipSha256")||!FileArtifacts.Token(ownedCopy["copyId"]?.GetValue<string>())||!FileArtifacts.Token(ownedCopy["ownershipSha256"]?.GetValue<string>(),64)||
            result["state"]?.GetValue<string>()!="owned_workplane_verified_observed"||result["recordedJournalState"]?.GetValue<string>()!="succeeded_verified"||
            !FileArtifacts.Token(result["bridgeRequestId"]?.GetValue<string>())||!FileArtifacts.Token(result["executionOperationId"]?.GetValue<string>())||
            !ArtifactProof.Boolean(result["copyFenceReleased"],true)||!ArtifactProof.Boolean(result["noCommandReplay"],true)||
            new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"}.Any(k=>!ArtifactProof.Boolean(result[k],false)))throw new ReferenceRejected(code);
        var time=LeaseTime.Remaining(ArtifactProof.String(result,"timestampUtc",code),utcNow(),code);
        var current=LeaseTime.PythonPrecision(utcNow());expiry=LeaseTime.PythonPrecision(expiry);
        if(expiry>time.Expiry||expiry>current.AddSeconds(300))throw new ReferenceRejected(code);
        double seconds=Math.Min(Math.Min(time.Remaining,300),(expiry-current).TotalSeconds);
        if(seconds<=0)throw new ReferenceRejected(code);
        return IssueLease(ownedCopy,seconds);
    }
    internal JsonObject IssueVerifiedWorkplaneActivation(JsonObject result,JsonObject ownedCopy,DateTimeOffset expiry)
    {
        const string code="verified_activation_copy_lease_invalid";
        if(!ArtifactProof.Fields(ownedCopy,"copyId","ownershipSha256")||!FileArtifacts.Token(ownedCopy["copyId"]?.GetValue<string>())||!FileArtifacts.Token(ownedCopy["ownershipSha256"]?.GetValue<string>(),64)||
            result["state"]?.GetValue<string>()!="owned_workplane_activation_verified_observed"||result["recordedJournalState"]?.GetValue<string>()!="succeeded_verified"||
            !FileArtifacts.Token(result["bridgeRequestId"]?.GetValue<string>())||!FileArtifacts.Token(result["executionOperationId"]?.GetValue<string>())||
            !ArtifactProof.Boolean(result["copyFenceReleased"],true)||!ArtifactProof.Boolean(result["noCommandReplay"],true)||
            new[]{"cadMutationAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"}.Any(k=>!ArtifactProof.Boolean(result[k],false)))throw new ReferenceRejected(code);
        var time=LeaseTime.Remaining(ArtifactProof.String(result,"timestampUtc",code),utcNow(),code);
        var current=LeaseTime.PythonPrecision(utcNow());expiry=LeaseTime.PythonPrecision(expiry);
        if(expiry>time.Expiry||expiry>current.AddSeconds(300))throw new ReferenceRejected(code);
        double seconds=Math.Min(Math.Min(time.Remaining,300),(expiry-current).TotalSeconds);
        if(seconds<=0)throw new ReferenceRejected(code);
        return IssueLease(ownedCopy,seconds);
    }
    private JsonObject IssueLease(JsonObject result,double seconds)
    {
        Purge();if(copies.Count>=128)Clear();var handle=Guid.NewGuid().ToString("N");
        copies[handle]=new(now()+seconds,new JsonObject{["copyId"]=result["copyId"]!.DeepClone(),["ownershipSha256"]=result["ownershipSha256"]!.DeepClone()});
        var issued=result.DeepClone().AsObject();issued["workingCopyHandle"]=handle;issued["workingCopyLifetimeSeconds"]=(int)seconds;return issued;
    }
    public JsonObject InspectParameters(string handle)
    {
        Purge();if(!copies.TryGetValue(handle,out var found))throw new ReferenceRejected("working_copy_handle_unknown_or_expired");
        return found.Parameters.DeepClone().AsObject();
    }
    public double Remaining(string handle){InspectParameters(handle);return copies[handle].Expires-now();}
}
