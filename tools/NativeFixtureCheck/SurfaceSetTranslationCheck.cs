using System.Text.Json.Nodes;
using PowerShapeMcp;
internal static class SurfaceSetTranslationCheck
{
    internal static int Run(string input,string output)
    {
        var rows=JsonNode.Parse(File.ReadAllText(input))!["rows"]!.AsArray();int cases=0;
        var control=rows.Single(r=>r!["fault"]!.GetValue<string>()=="control")!;string root=control["root"]!.GetValue<string>(),id=control["executionId"]!.GetValue<string>(),sha=control["executionSha256"]!.GetValue<string>();
        var plan=SurfaceSetTranslationProof.Execution(root,id,sha,true);cases++;
        var verified=SolidQuarterTurnProof.Raw(root,control["verificationId"]!.GetValue<string>(),control["verificationSha256"]!.GetValue<string>()).Value;SurfaceSetTranslationProof.Verified(root,verified,control["verificationArguments"]!.AsObject());cases++;
        var intermediate=rows.Single(r=>r!["fault"]!.GetValue<string>()=="execute_only")!;string intermediateRoot=intermediate["root"]!.GetValue<string>();var execution=SolidQuarterTurnProof.Raw(intermediateRoot,intermediate["executionId"]!.GetValue<string>(),intermediate["executionSha256"]!.GetValue<string>()).Value;SurfaceSetTranslationProof.Receipt(intermediateRoot,execution,intermediate["arguments"]!.AsObject());cases++;
        foreach(var row in rows.Where(r=>r!["fault"]!.GetValue<string>()is not("control"or"execute_only")))
        {
            bool rejected=false;try{SurfaceSetTranslationProof.Execution(row!["root"]!.GetValue<string>(),row["executionId"]!.GetValue<string>(),row["executionSha256"]!.GetValue<string>());}catch(ReferenceRejected){rejected=true;}if(!rejected)throw new InvalidDataException("SDK admitted incomplete/tampered set: "+row!["fault"]);cases++;
        }
        var before=ArtifactProof.Verified(root,"responses",plan["inspectionRequestId"]!.GetValue<string>(),plan["inspectionSha256"]!.GetValue<string>(),16000000);var after=ArtifactProof.Verified(root,"responses",control["verificationArguments"]!["inspectionRequestId"]!.GetValue<string>(),control["verificationArguments"]!["inspectionSha256"]!.GetValue<string>(),16000000);var original=plan["captureBefore"]!.AsObject();var current=verified["surfaceCaptureAfter"]!.AsObject();
        foreach(var fault in new[]{"target_position","other_position","normal","normal_ulp","visibility","target_order"})
        {
            var changed=current.DeepClone().AsObject();if(fault=="normal_ulp")changed["witnesses"]![0]!["samples"]!["normalsWorldRaw"]![0]![0]=2.220446049250313e-16;else if(fault=="visibility")changed["witnesses"]![0]!["samples"]!["visibility"]![0]=false;else if(fault=="target_order"){var x=changed["witnesses"]!.AsArray();var a=x[0]!.DeepClone();x[0]=x[1]!.DeepClone();x[1]=a;}else changed["witnesses"]![fault=="other_position"?2:0]!["samples"]![fault=="normal"?"normalsWorldRaw":"positionsWorldRaw"]![0]![0]=999;
            bool rejected=false;try{SurfaceSetTranslationProof.Post(before,after,original,changed,plan["vectorRaw"]!.AsArray(),SurfaceSetTranslationProof.Keys(plan));}catch(ReferenceRejected){rejected=true;}if(!rejected)throw new InvalidDataException("SDK accepted forged set witness: "+fault);cases++;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,new JsonObject{["state"]="passed",["cases"]=cases,["cadConnectionAttempted"]=false}.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"PASS: {cases} actual Framework/SDK set execution/verification/partial-step/witness proofs; no CAD.");return 0;
    }
}
