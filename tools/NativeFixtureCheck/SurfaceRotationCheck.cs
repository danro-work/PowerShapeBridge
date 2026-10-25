using System.Text.Json.Nodes;
using PowerShapeMcp;
internal static class SurfaceRotationCheck {
    internal static int Run(string input,string output){
        var rows=JsonNode.Parse(File.ReadAllText(input))!["rows"]!.AsArray();int count=0;foreach(var control in rows.Where(r=>r!["fault"]!.GetValue<string>().StartsWith("control",StringComparison.Ordinal))){var c=control!.AsObject();string root=c["root"]!.GetValue<string>(),id=c["executionId"]!.GetValue<string>(),sha=c["executionSha256"]!.GetValue<string>();var args=c["verificationArguments"]!.AsObject();var result=c["verificationResult"]!.AsObject();_=OwnedSurfaceRotationProof.Execution(root,id,sha);OwnedSurfaceRotationProof.Verified(root,result,args);count+=2;
        foreach(string fault in new[]{"position","normal","other_surface_ulp","wrong_sha"}){
            bool rejected=false;try{if(fault=="wrong_sha")_=OwnedSurfaceRotationProof.Execution(root,id,new string('f',64));else{var v=result.DeepClone().AsObject();var row=v["surfaceCaptureAfter"]!["witnesses"]![fault=="other_surface_ulp"?1:0]!["samples"]!;string field=fault=="normal"?"normalsWorldRaw":"positionsWorldRaw";row[field]![0]![0]=fault=="other_surface_ulp"?0.33333333333333343:999;OwnedSurfaceRotationProof.Verified(root,v,args);}}catch(ReferenceRejected){rejected=true;}if(!rejected)throw new InvalidDataException("Surface rotation forged proof accepted: "+fault);count++;
        }
        if(c["fault"]!.GetValue<string>()=="control_oblique")foreach(string fault in new[]{"angle","pivot","axis","profile"}){
            string mirror=Path.Combine(root,"surface-proof-forgery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(mirror);
            foreach(string folder in new[]{"responses","ownedexecutions","ownedrotations","rotationplans"}){Directory.CreateDirectory(Path.Combine(mirror,folder));foreach(var file in Directory.GetFiles(Path.Combine(root,folder),"*.json"))File.Copy(file,Path.Combine(mirror,folder,Path.GetFileName(file)));}
            _=OwnedSurfaceRotationProof.Execution(mirror,id,sha);count++;
            string planPath=Path.Combine(mirror,"ownedexecutions",id+".json"),rawPath=Path.Combine(mirror,"responses",id+".json");var plan=JsonNode.Parse(File.ReadAllText(planPath))!.AsObject();var raw=JsonNode.Parse(File.ReadAllText(rawPath))!.AsObject();
            if(fault=="angle")plan["angleDegrees"]=-29;else if(fault=="profile")plan["measurementProfile"]="owned_surface_grid_world_z90_v1";else{int index=fault=="pivot"?0:3;plan["nativeAxis"]![index]=plan["nativeAxis"]![index]!.GetValue<double>()+1e-8;}
            File.WriteAllText(planPath,plan.ToJsonString());string Sha(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();raw["plan"]=plan.DeepClone();raw["planSha256"]=Sha(planPath);File.WriteAllText(rawPath,raw.ToJsonString());
            bool rejected=false;try{_=OwnedSurfaceRotationProof.Execution(mirror,id,Sha(rawPath));}catch(ReferenceRejected){rejected=true;}if(!rejected)throw new InvalidDataException("Surface rotation source binding accepted forged "+fault);count++;
        }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,new JsonObject{["state"]="passed",["cases"]=count,["cadConnectionAttempted"]=false}.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"PASS: {count} actual Framework surface rotation receipts / SDK proof controls; no CAD.");return 0;
    }
}
