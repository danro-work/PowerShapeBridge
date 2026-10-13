using System.Text.Json.Nodes;
using PowerShapeMcp;

internal static class OwnedRotationCheck
{
    internal static int Run(string stagePath,string outputPath)
    {
        var stage=Path.GetFullPath(stagePath);var report=JsonNode.Parse(File.ReadAllText(Path.Combine(stage,"reports","owned-rotation-plan-tests.json")))!;var checks=new JsonArray();
        foreach(var item in report["rows"]!.AsArray())
        {
            var row=item!.AsObject();var root=Path.GetFullPath(row["root"]!.GetValue<string>());
            if(!root.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Fixture outside stage.");
            var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>FileArtifacts.Sha256(File.ReadAllBytes(path)));
            var candidate=new CandidateStore();var plans=new RotationPlanStore(root,candidate);var bindings=new DocumentBindingStore(root,candidate,new WorkingCopyStore());var store=new OwnedRotationStore(root,plans,bindings);
            var result=row["result"]!.DeepClone().AsObject();result["bridgeRequestId"]=row["requestId"]!.DeepClone();bool accepted=false;
            try{store.Receipt(result,row["parameters"]!.AsObject());accepted=true;}catch(ReferenceRejected){}
            var expected=row["fault"]!.GetValue<string>() is "control" or "mixed_targets";
            if(accepted!=expected)throw new InvalidDataException("SDK packet compatibility differs: "+row["fault"]);
            if(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length!=before.Count||before.Any(pair=>FileArtifacts.Sha256(File.ReadAllBytes(pair.Key))!=pair.Value))throw new InvalidDataException("SDK proof checker changed artifacts.");
            checks.Add(new JsonObject{["fault"]=row["fault"]!.DeepClone(),["accepted"]=accepted,["newHandleIssued"]=false});
        }
        if(checks.Count!=15)throw new InvalidDataException("Incomplete native matrix.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);File.WriteAllText(outputPath,new JsonObject{["state"]="passed",["cases"]=15,["cadConnectionAttempted"]=false,["scope"]="Actual native packet Receipt proof compatibility only; transport/scoped issue is separately tested",["checks"]=checks}.ToJsonString(new(){WriteIndented=true}));
        Console.WriteLine("PASS: 15 actual native owned rotation packet receipts checked by SDK; no CAD or artifact changes.");return 0;
    }
}
