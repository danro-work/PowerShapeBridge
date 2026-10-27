using System.Text.Json.Nodes;
using PowerShapeMcp;
internal static class SurfaceSetStopDispositionCheck
{
 static void Need(bool v,string why){if(!v)throw new InvalidDataException(why);}
 static string S(JsonObject o,string k)=>o[k]!.GetValue<string>();
 static void Refuses(Action test,string why){bool rejected=false;try{test();}catch(Exception e)when(e is ReferenceRejected or IOException){rejected=true;}Need(rejected,why);}
 internal static int Run(string input,string output)
 {
  var f=JsonNode.Parse(File.ReadAllText(input))!["fixture"]!.AsObject();string root=S(f,"root"),id=S(f,"executionId"),sha=S(f,"executionSha256"),old=S(f,"originalExecutionId"),oldSha=S(f,"originalExecutionSha256");string stage=Directory.GetParent(Directory.GetParent(root)!.FullName)!.FullName;Need(File.Exists(Path.Combine(stage,".surface-set-stop-disposition-fixture"))&&Path.GetFileName(root).StartsWith("owned-operation-self-test-",StringComparison.Ordinal),"Only explicitly marked synthetic root; never installed runtime.");
  var refs=f["preparationReferences"]!.AsObject();var p=SurfaceSetStopDispositionProof.Preparation(root,refs);var store=new SurfaceSetStopDispositionStore(root);var prep=SolidScaleProof.Raw(root,S(refs,"dispositionId"),S(refs,"preparationReceiptSha256")).Value;string handle=store.Issue(prep,p["originalArguments"]!.AsObject());Need(JsonNode.DeepEquals(store.Consume(handle),refs),"Consumed wrong disposition references.");int cases=2;Refuses(()=>store.Consume(handle),"One-use handle replay admitted.");cases++;
  var execution=SurfaceSetStopDispositionProof.Execution(root,id,sha);cases++;
  string copy=S(p,"copyId"),fence=Path.Combine(root,"workingcopies",copy,"mutation-fence.json"),secondary=Path.Combine(root,"workingcopies",copy,"surface-set-stop-disposition-fence.json"),archive=S(p,"archivePath");byte[] original=File.ReadAllBytes(fence);string secondaryText=File.ReadAllText(secondary);
  File.AppendAllText(secondary," ");Refuses(()=>SurfaceSetStopDispositionProof.Execution(root,id,sha),"Tampered secondary fence admitted.");File.WriteAllText(secondary,secondaryText);cases++;
  File.AppendAllText(archive," ");Refuses(()=>SurfaceSetStopDispositionProof.Execution(root,id,sha),"Tampered archive admitted.");File.WriteAllBytes(archive,original);cases++;
  var v=SolidScaleProof.Raw(root,S(f,"verificationId"),S(f,"verificationSha256")).Value;Refuses(()=>new WorkingCopyStore().IssueDisposition(v),"Fake context issued current copy lease.");cases++;
  // Simulate ONLY local final artifacts of qualified Native verification, in this marked fake root.
  var terminal=v.DeepClone().AsObject();terminal["state"]="surface_set_stop_disposition_verified_observed";terminal["nativeProviderQualified"]=true;terminal["copyFenceReleased"]=true;terminal["originalJournalPreserved"]=true;terminal["recordedJournalState"]="succeeded_verified";
  var resolution=new JsonObject{["schemaVersion"]=1,["profile"]=SurfaceSetStopDispositionProof.Profile,["originalExecutionId"]=old,["dispositionExecutionId"]=id,["dispositionReceiptSha256"]=sha,["verificationId"]=terminal["bridgeRequestId"]!.DeepClone(),["diagnosticSha256"]=terminal["diagnosticSha256"]!.DeepClone(),["archivePath"]=archive,["originalFenceSha256"]=p["originalFenceSha256"]!.DeepClone(),["originalSetSucceeded"]=false,["originalJournalPreserved"]=true,["originalDisposition"]="abandoned_partial_observed"};terminal["resolutionSha256"]=ArtifactProof.Freeze(root,Path.Combine("surfacesetstopresolutions",old+".json"),resolution);
  var j=ArtifactProof.Journal(root,id);var prior=j["events"]![4]!.AsObject();var evidence=new JsonArray();var args=f["verificationArguments"]!.AsObject();foreach(var pair in new[]{("executionRequestId","executionReceiptSha256"),("inspectionRequestId","inspectionSha256"),("listingRequestId","listingSha256")})evidence.Add(new JsonObject{["requestId"]=args[pair.Item1]!.DeepClone(),["sha256"]=args[pair.Item2]!.DeepClone()});
  _=ArtifactProof.Freeze(root,Path.Combine("operations",id,"000005.json"),new(){["schemaVersion"]=1,["operationId"]=id,["planSha256"]=execution.Receipt["planSha256"]!.DeepClone(),["sequence"]=5,["previousSha256"]=FileArtifacts.Sha256(File.ReadAllBytes(Path.Combine(root,"operations",id,"000004.json"))),["state"]="succeeded_verified",["timestampUtc"]=DateTimeOffset.UtcNow.ToString("o"),["details"]=new JsonObject{["evidence"]=evidence,["diagnosticSha256"]=terminal["diagnosticSha256"]!.DeepClone(),["resolutionSha256"]=terminal["resolutionSha256"]!.DeepClone(),["noCommandReplay"]=true,["originalSetSucceeded"]=false}});
  Refuses(()=>SurfaceSetStopDispositionProof.Execution(root,id,sha,true,terminal),"Terminal proof admitted while quarantine still present.");cases++;
  File.Delete(secondary);File.Delete(fence);_=SurfaceSetStopDispositionProof.Execution(root,id,sha,true,terminal);cases++;
  Refuses(()=>SurfaceSetPartialStopProof.Origin(root,old,oldSha),"Default old observer silently fell back to archive.");cases++;
  var forged=terminal.DeepClone().AsObject();forged["resolutionSha256"]=new string('0',64);Refuses(()=>SurfaceSetStopDispositionProof.Execution(root,id,sha,true,forged),"Unqualified archive mapping admitted.");cases++;
  // Restore synthetic quarantine; no installed or CAD files were involved.
  File.WriteAllBytes(fence,original);File.WriteAllText(secondary,secondaryText);
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllText(output,new JsonObject{["state"]="passed",["cases"]=cases,["cadConnectionAttempted"]=false,["actualFrameworkReceiptsChecked"]=true,["terminalTransitionSyntheticOnly"]=true,["liveFenceReleased"]=false}.ToJsonString(new(){WriteIndented=true}));Console.WriteLine($"PASS: {cases} Framework/SDK preparation, claim, archive, one-use and explicit terminal mapping checks; no CAD.");return 0;
 }
}
