using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerShapeMcp;

public sealed class DocumentHandleStore(Func<double>? clock=null,Func<DateTimeOffset>? utcClock=null)
{
    private readonly Func<double> now=clock??(()=> (double)Stopwatch.GetTimestamp()/Stopwatch.Frequency);
    private readonly Func<DateTimeOffset> utcNow=utcClock??(()=>DateTimeOffset.UtcNow);
    private sealed record Lease(double Expires,JsonObject Parameters);
    private readonly Dictionary<string,Lease> entries=[];
    public void Clear()=>entries.Clear();
    private void Purge(){var time=now();foreach(var id in entries.Where(p=>p.Value.Expires<=time).Select(p=>p.Key).ToArray())entries.Remove(id);}
    public JsonObject Issue(JsonObject listing,string receiptSha256)
    {
        try
        {
            var data=JsonSerializer.SerializeToElement(listing);
            if(ObservationPolicy.String(data,"state","document_listing_failed")!="documents_listed_verified")throw new ReferenceRejected("document_listing_failed");
            var request=ObservationPolicy.String(data,"bridgeRequestId","document_listing_receipt_invalid");
            if(!FileArtifacts.Token(request)||!FileArtifacts.Token(receiptSha256,64))throw new ReferenceRejected("document_listing_receipt_invalid");
            if(!data.TryGetProperty("documents",out var documents)||documents.ValueKind!=JsonValueKind.Array||documents.GetArrayLength()>256)throw new ReferenceRejected("document_inventory_invalid");
            var seen=new HashSet<long>();
            foreach(var document in documents.EnumerateArray())
            {
                if(document.ValueKind!=JsonValueKind.Object)throw new ReferenceRejected("document_inventory_invalid");
                var id=ObservationPolicy.Integer(document,"modelId","document_inventory_ambiguous");
                if(id<0||!seen.Add(id))throw new ReferenceRejected("document_inventory_ambiguous");
                if(document.TryGetProperty("activationSupported",out var supported)&&supported.ValueKind==JsonValueKind.True)
                {
                    var number=ObservationPolicy.Integer(document,"windowNumber","document_inventory_invalid");
                    if(number is <1 or >4096)throw new ReferenceRejected("document_inventory_invalid");
                }
            }
            Purge();if(entries.Count+documents.GetArrayLength()>1024)Clear();
            var issued=ObservationPolicy.String(data,"timestampUtc","document_listing_time_invalid");
            var time=LeaseTime.Remaining(issued,utcNow(),"document_listing_expired");
            var result=listing.DeepClone().AsObject();
            result["documentHandleLifetimeSeconds"]=Math.Min(300,(int)time.Remaining);
            result["documentHandleQualification"]="One-use choice of an already-open document in this connection. Native window IDs/numbers are not persistent; full listing is rechecked before activation.";
            foreach(var value in result["documents"]!.AsArray())
            {
                var document=value!.AsObject();
                if(document["activationSupported"] is not JsonValue supported||!supported.TryGetValue<bool>(out var activation)||!activation){document["documentHandle"]=null;continue;}
                var handle=Guid.NewGuid().ToString("N");
                entries[handle]=new(now()+time.Remaining,new JsonObject
                {
                    ["listingRequestId"]=request,["listingSha256"]=receiptSha256,
                    ["windowNumber"]=document["windowNumber"]!.DeepClone(),["modelId"]=document["modelId"]!.DeepClone(),
                    ["expiresUtc"]=LeaseTime.Format(time.Expiry)
                });
                document["documentHandle"]=handle;
            }
            return result;
        }
        catch{Clear();throw;}
    }
    public JsonObject Consume(string handle)
    {
        Purge();if(!entries.Remove(handle,out var entry))throw new ReferenceRejected("document_handle_unknown_expired_or_consumed");
        return entry.Parameters.DeepClone().AsObject();
    }
}
