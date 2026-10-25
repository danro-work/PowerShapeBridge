using System.Text.Json.Nodes;
using PowerShapeMcp;
internal static class SurfaceRotationCheck {
    internal static int Run(string input,string output){
        var rows=JsonNode.Parse(File.ReadAllText(input))!["rows"]!.AsArray();var c=rows.Single(r=>r!["fault"]!.GetValue<string>()=="control")!.AsObject();string root=c["root"]!.GetValue<string>(),id=c["executionId"]!.GetValue<string>(),sha=c["executionSha256"]!.GetValue<string>();var args=c["verificationArguments"]!.AsObject();var result=c["verificationResult"]!.AsObject();_=OwnedSurfaceRotationProof.Execution(root,id,sha);OwnedSurfaceRotationProof.Verified(root,result,args);int count=2;
        foreach(string fault in new[]{"position","normal","other_surface_ulp","wrong_sha"}){
            bool rejected=false;try{if(fault=="wrong_sha")_=OwnedSurfaceRotationProof.Execution(root,id,new string('f',64));else{var v=result.DeepClone().AsObject();var row=v["surfaceCaptureAfter"]!["witnesses"]![fault=="other_surface_ulp"?1:0]!["samples"]!;string field=fault=="normal"?"normalsWorldRaw":"positionsWorldRaw";row[field]![0]![0]=fault=="other_surface_ulp"?0.33333333333333343:999;OwnedSurfaceRotationProof.Verified(root,v,args);}}catch(ReferenceRejected){rejected=true;}if(!rejected)throw new InvalidDataException("Surface rotation forged proof accepted: "+fault);count++;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,new JsonObject{["state"]="passed",["cases"]=count,["cadConnectionAttempted"]=false}.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"PASS: {count} actual Framework surface rotation receipts / SDK proof controls; no CAD.");return 0;
    }
}
