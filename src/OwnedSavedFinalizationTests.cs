using System;
using System.Collections.Generic;
using System.IO;

namespace PowerShapeBridge {
 internal static class OwnedSavedFinalizationTests {
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".owned-saved-finalization-fixture")))throw new InvalidOperationException("Isolated finalization fixture required; no CAD.");int n=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Finalization guard failed "+n);n++;};Action<Action> rejects=f=>{bool denied=false;try{f();}catch(Exception e){if(!(e is IOException||e is InvalidDataException||e is ArgumentException||e is InvalidOperationException||e is UnauthorizedAccessException))throw;denied=true;}check(denied);};
   var good=new OwnedSavedReopenTests.Fixture();string root=good.Save.Fixture.Root,id=Guid.NewGuid().ToString("N");var prepared=OwnedSavedReopen.Prepare(root,id,good.Args);string rawSha=NativeJournal.Freeze(Path.Combine(root,"responses",id+".json"),prepared);var refs=new Dictionary<string,object>{{"seedPreparationRequestId",id},{"seedPreparationReceiptSha256",rawSha}};var before=OwnedSaveAs.SavedProof(good.Save.ResultPath);var plan=(Dictionary<string,object>)prepared["reopen"];var seed=(Dictionary<string,object>)plan["seed"];
   using(var seal=new SavedFinalizationSeal(root,refs)){seal.Recheck();check(Equals(seal.Plan["savedResult"],plan["savedResult"])||SavedFinalizationSeal.Same(seal.Plan["savedResult"],plan["savedResult"]));File.WriteAllText(good.Save.ResultPath,"Owned writer finalized to different saved serialization");seal.Recheck();check(!Equals(SavedFinalizationSeal.WriterAfter(before)["sha256"],before["sha256"]));rejects(()=>File.WriteAllText((string)seed["path"],"No write to sealed output"));}
   using(var detached=new SavedFinalizationSeal(root,refs)){detached.Recheck();check(true);} // Changed A never recertifies old SaveAs SHA.
   string checkpoint=(string)plan["checkpointPath"];File.SetAttributes(checkpoint,FileAttributes.Normal);rejects(()=>{using(var bad=new SavedFinalizationSeal(root,refs)){} });File.SetAttributes(checkpoint,FileAttributes.ReadOnly);
   var badRefs=new Dictionary<string,object>(refs);badRefs["seedPreparationReceiptSha256"]=new string('a',64);rejects(()=>{using(var bad=new SavedFinalizationSeal(root,badRefs)){} });
   var args=new Dictionary<string,object>(refs);foreach(string k in new[]{"observationRequestId","inspectionRequestId","listingRequestId"})args[k]=new string('a',32);foreach(string k in new[]{"observationReceiptSha256","inspectionSha256","listingSha256"})args[k]=new string('b',64);args["expiresUtc"]=DateTime.UtcNow.AddSeconds(200).ToString("o");args["finalizeOwnedSavedResult"]=true;OwnedSavedFinalization.Validate(args,false);check(true);
   foreach(object wrong in new object[]{false,"true",1,null}){var bad=new Dictionary<string,object>(args);bad["finalizeOwnedSavedResult"]=wrong;rejects(()=>OwnedSavedFinalization.Validate(bad,false));}
   var injected=new Dictionary<string,object>(args){{"command","FILE CLOSE ALL"}};rejects(()=>OwnedSavedFinalization.Validate(injected,false));var noSeed=new Dictionary<string,object>(args);noSeed.Remove("seedPreparationRequestId");rejects(()=>OwnedSavedFinalization.Validate(noSeed,false));
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));check(Program.ParseConfig(Program.Json.Serialize(config)).ownedSavedFinalizationExperimental);
   foreach(object wrong in new object[]{"true",1,null}){var bad=new Dictionary<string,object>(config);bad["ownedSavedFinalizationExperimental"]=wrong;rejects(()=>Program.ParseConfig(Program.Json.Serialize(bad)));}
   foreach(string dependency in new[]{"ownedSavedReopenExperimental","ownedSavedResultRestoreExperimental","workingCopiesExperimental"}){var bad=new Dictionary<string,object>(config);bad[dependency]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(bad)));}
   string target=new string('e',32),fence=OwnedSavedFinalization.Marker(Program.Root,target,"result-finalize-fence.json");Directory.CreateDirectory(Path.GetDirectoryName(fence));File.WriteAllText(fence,"{}");rejects(()=>OwnedSavedResultClose.NoPending(Program.Root,target));File.Delete(fence);OwnedSavedResultClose.NoPending(Program.Root,target);check(true);
   var refusal=OwnedSavedFinalization.Run(Guid.NewGuid().ToString("N"),OwnedSavedFinalization.Recipes[0],args);check(Equals(refusal["powerShapeConnectionAttempted"],false)&&Equals(refusal["contextChangeAttempted"],false));
   var envelope=new{requestId=id,operation="recipe.prepare",arguments=new{recipeId=OwnedSavedFinalization.Recipes[0],parameters=args}};FileRequests.Parse(id,Program.Json.Serialize(envelope));check(true);
   return n;
  }
 }
}
