using System.Text.Json.Nodes;
using PowerShapeMcp;

internal static class CheckpointOpenCheck
{
    private static void Require(bool ok,string reason){if(!ok)throw new InvalidDataException(reason);}
    internal static int Run(string stagePath,string outputPath)
    {
        var stage=Path.GetFullPath(stagePath);var report=JsonNode.Parse(File.ReadAllText(Path.Combine(stage,"reports","checkpoint-recovered-open.json")))!;
        var checks=new JsonArray();
        foreach(var entry in report["rows"]!.AsArray())
        {
            var row=entry!.AsObject();var root=Path.GetFullPath(row["root"]!.GetValue<string>());
            Require(root.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Fixture root outside stage.");
            var before=Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>FileArtifacts.Sha256(File.ReadAllBytes(path)));
            var copyId=row["copyId"]!.GetValue<string>();var recovered=row["recovered"]!.DeepClone().AsObject();recovered["bridgeRequestId"]=copyId;
            var current=LeaseTime.Parse(recovered["timestampUtc"]!.GetValue<string>(),"fixture_time");double time=100;
            using var session=new BridgeSession(new(root,TimeSpan.FromSeconds(30),false),()=>time,()=>current);
            var intent=row["intent"]!.AsObject();var source=new JsonObject{["recoveryId"]=intent["recoveryId"]!.DeepClone(),["recoverySha256"]=intent["recoverySha256"]!.DeepClone()};
            var initial=session.Copies.Issue(session.CheckpointRecoveries.CheckCreated(recovered,source));
            Require(initial["workingCopyHandle"] is not null&&initial["workingDocumentBindingHandle"] is null,"Disk copy granted wrong authority.");
            var verified=row["verification"]!.DeepClone().AsObject();var parameters=row["verificationParameters"]!.AsObject();
            string? bindingHandle=null;
            if(row["fault"]!.GetValue<string>()=="control")
            {
                Require(verified["state"]!.GetValue<string>()=="owned_open_verified_observed","Native OPEN not verified.");
                var opened=session.Copies.IssueVerifiedOpen(verified);var copy=opened["workingCopyHandle"]!.GetValue<string>();
                var inspection=ArtifactProof.Verified(root,"responses",parameters["inspectionRequestId"]!.GetValue<string>(),parameters["inspectionSha256"]!.GetValue<string>(),16777216);
                current=LeaseTime.Parse(inspection["timestampUtc"]!.GetValue<string>(),"fixture_time");time+=1;
                var candidate=session.Candidates.Issue(inspection,parameters["inspectionSha256"]!.GetValue<string>())["candidateDocumentHandle"]!.GetValue<string>();
                var input=session.Bindings.PrepareParameters(candidate,copy);var binding=row["binding"]!.DeepClone().AsObject();
                var expiry=LeaseTime.Parse(binding["binding"]!["expiresUtc"]!.GetValue<string>(),"fixture_time");input["expiresUtc"]=LeaseTime.Format(expiry);
                Require(input.All(pair=>JsonNode.DeepEquals(binding["binding"]![pair.Key],pair.Value)),"Native fixture parameter mismatch: "+string.Join(",",input.Where(pair=>!JsonNode.DeepEquals(binding["binding"]![pair.Key],pair.Value)).Select(pair=>pair.Key)));
                var issued=session.Bindings.Issue(binding,input,candidate,copy,expiry);bindingHandle=issued["workingDocumentBindingHandle"]!.GetValue<string>();
                Require(session.Bindings.ValidationParameters(bindingHandle)["bindingId"]!.GetValue<string>()==row["bindingId"]!.GetValue<string>(),"New binding does not belong to recovered copy.");
                Require(inspection["model"]!["id"]!.GetValue<int>()==500,"Expected current model ID.");
            }
            else
            {
                bool rejected=false;try{session.Copies.IssueVerifiedOpen(verified);}catch(ReferenceRejected){rejected=true;}Require(rejected,"Failed verification issued a working lease.");
                Require(row["binding"] is null,"Native failure produced binding.");
            }
            Require(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length==before.Count&&before.All(pair=>FileArtifacts.Sha256(File.ReadAllBytes(pair.Key))==pair.Value),"SDK proof validation changed artifacts.");
            checks.Add(new JsonObject{["fault"]=row["fault"]!.DeepClone(),["copyId"]=copyId,["workingDocumentBindingHandle"]=bindingHandle,["originOutcomeResolved"]=false,["cadConnectionAttempted"]=false});
        }
        Require(checks.Count==6,"Insufficient recovered-copy scenarios.");
        var result=new JsonObject{["state"]="passed",["cases"]=checks.Count,["cadConnectionAttempted"]=false,["artifactWritesAttempted"]=false,["checks"]=checks};
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);File.WriteAllText(outputPath,result.ToJsonString(new(){WriteIndented=true}));
        Console.WriteLine("PASS: SDK recovered-copy/verified OPEN/current binding proofs; six native scenarios, no CAD.");return 0;
    }
}
