using System.Text.Json.Nodes;

namespace PowerShapeMcp;

internal sealed class SolidScaleStore(string root,OwnedUniformScaleStore packets,bool surface=false)
{
    readonly Dictionary<string,JsonObject> pinned=[];
    internal void Clear()=>pinned.Clear();
    internal void Revoke(string handle){pinned.Remove(handle);packets.Revoke(handle);}
    internal void Pin(string handle,JsonObject proof)
    {
        var fresh=SolidScaleProof.Preparation(root,packets.ValidationParameters(handle),surface);
        if(!JsonNode.DeepEquals(proof,fresh)||pinned.Count>=128)throw new ReferenceRejected("solid_scale_preparation_pin_invalid");pinned.Add(handle,proof.DeepClone().AsObject());
    }
    internal JsonObject Consume(string handle,CancellationToken cancellationToken)
    {
        if(!pinned.TryGetValue(handle,out var proof))throw new ReferenceRejected("solid_scale_handle_unknown_or_consumed");var fresh=SolidScaleProof.Preparation(root,packets.ValidationParameters(handle),surface);
        if(!JsonNode.DeepEquals(proof,fresh))throw new ReferenceRejected("solid_scale_preparation_changed");cancellationToken.ThrowIfCancellationRequested();pinned.Remove(handle);packets.Revoke(handle);return proof.DeepClone().AsObject();
    }
}
