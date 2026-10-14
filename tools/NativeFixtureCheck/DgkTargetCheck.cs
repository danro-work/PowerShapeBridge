using System.Text.Json.Nodes;
using PowerShapeMcp;

internal static class DgkTargetCheck
{
    internal static int Run(string stage,string output)
    {
        stage=Path.GetFullPath(stage);var report=JsonNode.Parse(File.ReadAllText(Path.Combine(stage,"reports","owned-dgk-target-tests.json")))!.AsObject();
        var checkedRows=new JsonArray();var controls=new HashSet<string>{"primary","curve","mixed","all_companions","working_byte_capture","caller_mutation","held_writer","held_writer_truncate","held_writer_mapped"};
        var mismatch=0;
        foreach(var node in report["rows"]!.AsArray())
        {
            var row=node!.AsObject();if(row["result"]!["state"]!.GetValue<string>()!="owned_dgk_target_prepared_files")continue;
            var root=Path.GetFullPath(row["root"]!.GetValue<string>());if(!root.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Fixture outside stage");
            var result=row["result"]!.DeepClone().AsObject();result["bridgeRequestId"]=result["targetId"]!.DeepClone();
            var time=LeaseTime.Parse(result["target"]!["preparedUtc"]!.GetValue<string>(),"fixture_time_invalid").AddMilliseconds(50);
            var candidates=new CandidateStore(utcClock:()=>time);var copies=new WorkingCopyStore(utcClock:()=>time);var bindings=new DocumentBindingStore(root,candidates,copies,utcClock:()=>time);
            var store=new OwnedDgkTargetStore(root,candidates,bindings,utcClock:()=>time);var accepted=false;string? error=null;
            try{store.Receipt(result,row["parameters"]!.AsObject());accepted=true;}
            catch(Exception failure)when(failure is ReferenceRejected or IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException){error=failure.Message;}
            var expected=controls.Contains(row["fault"]!.GetValue<string>());if(accepted!=expected)mismatch++;
            checkedRows.Add(new JsonObject{["fault"]=row["fault"]!.DeepClone(),["expectedAccepted"]=expected,["accepted"]=accepted,["error"]=error});
        }
        if(checkedRows.Count!=23||checkedRows.Select(row=>row!["fault"]!.GetValue<string>()).Distinct().Count()!=23)throw new InvalidDataException("Incomplete native target compatibility matrix");
        var audit=new JsonObject{["state"]=mismatch==0?"passed":"failed",["cases"]=checkedRows.Count,["mismatches"]=mismatch,["cadConnectionAttempted"]=false,["rows"]=checkedRows};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,audit.ToJsonString(new(){WriteIndented=true}));
        Console.WriteLine($"Native DGK target SDK compatibility: {checkedRows.Count} cases; {mismatch} mismatches; no CAD.");return mismatch==0?0:1;
    }
}
