using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace PowerShapeMcp;
internal static class OwnedSurfaceSelectionProof
{
    internal const string Profile="owned_surface_grid_selection_v1";
    internal static bool Is(JsonObject p)=>p["mode"]?.GetValue<string>()is "select_surface_single" or "clear_surface_single";
    internal static bool IsCapture(JsonObject c)=>c["profile"]?.GetValue<string>()==Profile;
    static void Need(bool x)=>OwnedSelectionProof.Need(x);
    internal static void Parameters(JsonObject p,bool native=false)
    {
        Need(Is(p)&&ArtifactProof.Fields(p,"mode",native?"entityKey":"candidateEntityHandle","grid"));
        if(native){var key=p["entityKey"]!.AsArray();Need(key.Count==2&&ArtifactProof.IntegerIs(key[0],21)&&SurfaceReadTools.Integer(key[1])>0);}
        else Need(FileArtifacts.Token(OwnedSelectionProof.Text(p,"candidateEntityHandle")));
        SurfaceReadTools.Grid(p["grid"]!.AsObject());Need(SurfaceReadTools.Integer(p["grid"]!["tDivisions"])<=4&&SurfaceReadTools.Integer(p["grid"]!["uDivisions"])<=4);
    }
    static void Layout(JsonNode? value,int scalarCount,params int[] dimensions)
    {
        var v=value!.AsObject();if(v["kind"]?.GetValue<string>()=="list"){Need(v.Count==1);return;}
        Need(ArtifactProof.Fields(v,"kind","rank","lengths","lowerBounds")&&v["kind"]!.GetValue<string>()=="array");int rank=SurfaceReadTools.Integer(v["rank"]);var lengths=v["lengths"]!.AsArray();var lower=v["lowerBounds"]!.AsArray();Need(rank==lengths.Count&&rank==lower.Count&&(rank==1||rank==dimensions.Length));
        for(int i=0;i<rank;i++){Need(SurfaceReadTools.Integer(lengths[i])==(rank==1?scalarCount:dimensions[i]));_=SurfaceReadTools.Integer(lower[i]);}
    }
    internal static void Capture(JsonObject c,JsonObject parameters,JsonObject source,int expectedCount)
    {
        Parameters(parameters,true);Need(IsCapture(c)&&ArtifactProof.Fields(c,"profile","parameters","count","entityKey","bodyName","compositeId","bounds","boundsStatus","witnesses","proofMode","nullGetterInferredEmpty")&&JsonNode.DeepEquals(c["parameters"],parameters)&&JsonNode.DeepEquals(c["entityKey"],parameters["entityKey"]));
        Need(ArtifactProof.IntegerIs(c["count"],expectedCount)&&ArtifactProof.Boolean(c["nullGetterInferredEmpty"],false)&&c["bounds"]is null&&c["boundsStatus"]?.GetValue<string>()=="unavailable_sample_witness_only"&&c["proofMode"]?.GetValue<string>()==(expectedCount==0?"explicit_double_count_zero":"native_selected_items_metadata"));
        var inventory=source["entities"]!.AsArray();Need(inventory.Count is >=1 and <=8&&inventory.All(e=>ArtifactProof.IntegerIs(e!["nativeTypeCode"],21))&&source["lengthUnitsRaw"]?.GetValue<string>()=="mm"&&SurfaceReadTools.Number(source["lengthUnitFactorRaw"])==1);
        Need(SurfaceReadTools.Integer(c["compositeId"])>0);var target=inventory.Single(e=>JsonNode.DeepEquals(e!["nativeId"],c["entityKey"]![1]));Need(JsonNode.DeepEquals(target!["name"],c["bodyName"])&&Regex.IsMatch(OwnedSelectionProof.Text(c,"bodyName"),"\\A[A-Za-z0-9_]{1,64}\\z"));
        var grid=parameters["grid"]!.AsObject();int tp=SurfaceReadTools.Integer(grid["tDivisions"])+1,up=SurfaceReadTools.Integer(grid["uDivisions"])+1,n=tp*up;var rows=c["witnesses"]!.AsArray();Need(rows.Count==inventory.Count);var keys=new HashSet<int>();
        foreach(var row in rows){var r=row!.AsObject();Need(ArtifactProof.Fields(r,"entityKey","name","samples")&&ArtifactProof.IntegerIs(r["entityKey"]![0],21));int id=SurfaceReadTools.Integer(r["entityKey"]![1]);Need(keys.Add(id));var e=inventory.Single(e=>ArtifactProof.IntegerIs(e!["nativeId"],id));Need(JsonNode.DeepEquals(e!["name"],r["name"]));var sample=r["samples"]!.AsObject();Need(ArtifactProof.Fields(sample,"shape","nativeArrayLayout","positionsWorldRaw","normalsWorldRaw","visibility"));
            var shape=sample["shape"]!.AsObject();Need(ArtifactProof.Fields(shape,"tPoints","uPoints","pointCount","order")&&ArtifactProof.IntegerIs(shape["tPoints"],tp)&&ArtifactProof.IntegerIs(shape["uPoints"],up)&&ArtifactProof.IntegerIs(shape["pointCount"],n)&&shape["order"]?.GetValue<string>()=="native_reported_order");
            foreach(var field in new[]{"positionsWorldRaw","normalsWorldRaw"}){var points=sample[field]!.AsArray();Need(points.Count==n);foreach(var point in points){var p=point!.AsArray();Need(p.Count==3);foreach(var v in p)_=SurfaceReadTools.Number(v);}}
            var flags=sample["visibility"]!.AsArray();Need(flags.Count==n);foreach(var flag in flags)Need(flag is JsonValue v&&v.TryGetValue<bool>(out _));
            var layout=sample["nativeArrayLayout"]!.AsObject();Need(ArtifactProof.Fields(layout,"response","positions","normals","visibility"));Layout(layout["response"],4);Layout(layout["positions"],3*n,tp,up,3);Layout(layout["normals"],3*n,tp,up,3);Layout(layout["visibility"],n,tp,up);
        }
    }
    internal static void Preserved(JsonObject before,JsonObject after)
    {
        foreach(var k in new[]{"profile","parameters","entityKey","bodyName","compositeId","bounds","boundsStatus","witnesses"})Need(JsonNode.DeepEquals(before[k],after[k]));
    }
}
