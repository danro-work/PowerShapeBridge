using System.Text.Json.Nodes;

namespace PowerShapeMcp;

// Pins the exact native preparation response at issuance, in this connection.
// The underlying owned packet supplies expiry/dependency checks; it cannot by
// itself authorize this execution path without this additional pinned receipt.
internal sealed class SolidQuarterTurnStore(string root,OwnedRotationStore rotations,Func<DateTimeOffset>? utcClock=null,bool general=false)
{
    readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    sealed record Pinned(JsonObject Parameters,DateTimeOffset Expires);
    readonly Dictionary<string,Pinned> proofs=[];
    internal void Clear()=>proofs.Clear();
    internal void Revoke(string handle){proofs.Remove(handle);rotations.Revoke(handle);}
    internal void Pin(string handle,JsonObject parameters)
    {
        var checkedProof=SolidQuarterTurnProof.Preparation(root,rotations.ValidationParameters(handle),general);
        foreach(var expired in proofs.Where(p=>p.Value.Expires<=utcNow()).Select(p=>p.Key).ToArray())proofs.Remove(expired);
        if(!JsonNode.DeepEquals(checkedProof,parameters)||proofs.Count>=128)throw new ReferenceRejected("solid_quarter_turn_preparation_pin_invalid");
        var packet=ArtifactProof.Verified(root,"ownedrotations",parameters["ownedRotationId"]!.GetValue<string>(),parameters["ownedRotationSha256"]!.GetValue<string>(),65536);
        proofs.Add(handle,new(parameters.DeepClone().AsObject(),LeaseTime.Parse(packet["expiresUtc"]!.GetValue<string>(),"solid_quarter_turn_expired")));
    }
    internal JsonObject Consume(string handle,CancellationToken cancellationToken=default)
    {
        if(!proofs.TryGetValue(handle,out var pinned))throw new ReferenceRejected("solid_quarter_turn_handle_unknown_or_consumed");
        var current=SolidQuarterTurnProof.Preparation(root,rotations.ValidationParameters(handle),general);
        if(!JsonNode.DeepEquals(current,pinned.Parameters))throw new ReferenceRejected("solid_quarter_turn_preparation_receipt_changed");
        cancellationToken.ThrowIfCancellationRequested();proofs.Remove(handle);rotations.Revoke(handle);return pinned.Parameters.DeepClone().AsObject();
    }
}
