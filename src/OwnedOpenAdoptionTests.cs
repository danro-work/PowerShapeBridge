using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 public static class OwnedOpenAdoptionTests {
  sealed class ReadContext : DocumentBindings.IContext {
   internal Dictionary<string,object> Listing,Snapshot;
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> Observation(){return Clone(Snapshot);}
   public void ConfirmDocument(){}
   public void Dispose(){}
  }
  static Dictionary<string,object> Clone(Dictionary<string,object> d){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(d));}
  public static int SelfTest(){
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("OPEN adoption check failed at "+count);count++;};Action<Action> rejects=action=>{bool refused=false;try{action();}catch(InvalidDataException){refused=true;}catch(InvalidOperationException){refused=true;}catch(IOException){refused=true;}catch(ArgumentException){refused=true;}check(refused);};
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));check(Program.ParseConfig(Program.Json.Serialize(config)).ownedOpenAdoptionExperimental);
   var disabled=Clone(config);disabled.Remove("ownedOpenAdoptionExperimental");check(!Program.ParseConfig(Program.Json.Serialize(disabled)).ownedOpenAdoptionExperimental);
   var coerced=Clone(config);coerced["ownedOpenAdoptionExperimental"]="true";rejects(()=>Program.ParseConfig(Program.Json.Serialize(coerced)));
   var missing=Clone(config);missing["workingCopyOpenExperimental"]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(missing)));
   string root=Path.Combine(Program.Root,"work","adoption-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);foreach(string dir in new[]{"responses","requests","copyopenplans","operations"})Directory.CreateDirectory(Path.Combine(root,dir));
   string source=Path.Combine(root,"Source.psmodel");File.WriteAllText(source,"Synthetic bytes; never opened as CAD.");string copyId=Guid.NewGuid().ToString("N"),opened=Guid.NewGuid().ToString("N");var copy=OwnedCopies.Create(root,copyId,source,Program.Hash(source));var owner=(Dictionary<string,object>)copy["ownership"];string working=(string)((Dictionary<string,object>)owner["working"])["path"];
   string time=DateTime.UtcNow.AddMinutes(-10).ToString("o"),oldInspect=Guid.NewGuid().ToString("N"),oldListing=Guid.NewGuid().ToString("N");
   var snapshot=new Dictionary<string,object>();foreach(string k in new[]{"levelsContainingEntities","selectionBefore","selectionAfter","workplanes","entities"})snapshot[k]=new object[0];foreach(string k in new[]{"lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})snapshot[k]=null;
   snapshot["state"]="succeeded_verified";snapshot["modelScope"]="active_document_read_only";snapshot["timestampUtc"]=time;snapshot["processId"]=1;snapshot["processStartedUtc"]="2026-10-09T10:00:00Z";snapshot["windowsSessionId"]=1;snapshot["productVersion"]="23.2.2";snapshot["model"]=new Dictionary<string,object>{{"id",9},{"name","Source"},{"path",source}};snapshot["entityCount"]=0;snapshot["contextBefore"]=new[]{0,0};snapshot["contextAfter"]=new[]{0,0};
   var window=new Dictionary<string,object>{{"windowNumber",1},{"nativeWindowId",3},{"type","Model"},{"selectedRaw",1},{"modelId",9},{"modelName","Source"},{"modelPath",source}};
   var listing=new Dictionary<string,object>{{"state","documents_listed_verified"},{"timestampUtc",time},{"processId",1},{"processStartedUtc",snapshot["processStartedUtc"]},{"windowsSessionId",1},{"windowCount",1},{"windows",new[]{window}},{"activeWindow",window}};
   string oldInspectSha=NativeJournal.Freeze(Path.Combine(root,"responses",oldInspect+".json"),snapshot),oldListingSha=NativeJournal.Freeze(Path.Combine(root,"responses",oldListing+".json"),listing);
   var parameters=new Dictionary<string,object>{{"copyId",copyId},{"ownershipSha256",copy["ownershipSha256"]},{"inspectionRequestId",oldInspect},{"inspectionSha256",oldInspectSha},{"listingRequestId",oldListing},{"listingSha256",oldListingSha},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}};
   var plan=new Dictionary<string,object>(parameters){{"schemaVersion",1},{"operationId",opened},{"operation","open_working_copy"},{"bridgeVersion",Program.ReleaseVersion},{"createdUtc",time},{"geometryExecutionEnabled",false},{"workingPath",working}};
   string planSha=NativeJournal.Freeze(Path.Combine(root,"copyopenplans",opened+".json"),plan);
   NativeJournal.Freeze(Path.Combine(root,"requests",opened+".json.processing.done"),new Dictionary<string,object>{{"requestId",opened},{"operation","recipe.prepare"},{"arguments",new Dictionary<string,object>{{"recipeId","open_working_copy"},{"parameters",parameters}}}});
   var journal=new NativeJournal(root,opened,planSha);journal.Append("prepared",new Dictionary<string,object>{{"operation","open_working_copy"},{"contextChangeAttempted",false}});journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",opened},{"copyId",copyId}});journal.Append("command_started",new Dictionary<string,object>{{"commandSequence","FILE OPENPATH / fixed PATHNAME / ACCEPT"},{"contextChangeAttempted",true}});
   string claimPath=Path.Combine(Path.GetDirectoryName(working),"open-once.json");NativeJournal.Freeze(claimPath,new Dictionary<string,object>{{"operationId",opened},{"planSha256",planSha},{"copyId",copyId},{"ownershipSha256",copy["ownershipSha256"]},{"automaticReplayForbidden",true}});
   string receiptSha=NativeJournal.Freeze(Path.Combine(root,"responses",opened+".json"),new Dictionary<string,object>{{"state","timeout_outcome_unknown"},{"workerPid",7654321},{"workerTimeoutSeconds",110},{"operation","--mcp-operation "+opened},{"error",new Dictionary<string,object>{{"code","worker_timeout"}}}});
   snapshot["model"]=new Dictionary<string,object>{{"id",10},{"name",Path.GetFileNameWithoutExtension(working)},{"path",working}};snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");window["selectedRaw"]=0;
   var added=new Dictionary<string,object>{{"windowNumber",2},{"nativeWindowId",8},{"type","Model"},{"selectedRaw",1},{"modelId",10},{"modelName",Path.GetFileNameWithoutExtension(working)},{"modelPath",working}};
   listing["windows"]=new[]{window,added};listing["activeWindow"]=added;listing["windowCount"]=2;listing["timestampUtc"]=DateTime.UtcNow.ToString("o");
   string inspection=Guid.NewGuid().ToString("N"),listed=Guid.NewGuid().ToString("N");string inspectionSha=NativeJournal.Freeze(Path.Combine(root,"responses",inspection+".json"),snapshot),listingSha=NativeJournal.Freeze(Path.Combine(root,"responses",listed+".json"),listing);
   var args=new Dictionary<string,object>{{"openRequestId",opened},{"openReceiptSha256",receiptSha},{"openPlanSha256",planSha},{"inspectionRequestId",inspection},{"inspectionSha256",inspectionSha},{"listingRequestId",listed},{"listingSha256",listingSha},{"windowNumber",2},{"modelId",10},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}};
   Action proof=()=>{using(var held=new OwnedOperationPlans.Proofs()){new OwnedOpenAdoption.Interrupted(root,args,held,pid=>false);}};
   OwnedOpenAdoption.Validate(args);count++;proof();count++;rejects(()=>{using(var held=new OwnedOperationPlans.Proofs()){new OwnedOpenAdoption.Interrupted(root,args,held,pid=>true);}});
   rejects(()=>NativeJournal.ForVerification(root,opened,planSha));
   var originals=Directory.GetFiles(Path.Combine(root,"operations",opened)).ToDictionary(p=>p,p=>Program.Hash(p));string claimSha=Program.Hash(claimPath);
   var observed=OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false);
   check(Equals(observed["state"],"owned_open_document_observed_unqualified")&&Equals(observed["nativeProviderQualified"],false)&&Equals(observed["originalCommandReturned"],false)&&Equals(observed["historicalSavedBytesCertified"],false));
   check(!File.Exists(Path.Combine(root,"openadoptions",opened+".once.json"))&&originals.All(p=>Program.Hash(p.Key)==p.Value)&&Program.Hash(claimPath)==claimSha);rejects(()=>NativeJournal.ForVerification(root,opened,planSha));
   var extra=Clone(args);extra["command"]="OPEN";rejects(()=>OwnedOpenAdoption.Validate(extra));var wrong=Clone(args);wrong["openPlanSha256"]=new string('0',64);rejects(()=>{using(var held=new OwnedOperationPlans.Proofs()){new OwnedOpenAdoption.Interrupted(root,wrong,held,pid=>false);}});
   foreach(string file in originals.Keys){byte[] bytes=File.ReadAllBytes(file);var eventData=Clone(Program.Json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(bytes)));eventData["previousSha256"]=new string('0',64);File.WriteAllText(file,Program.Json.Serialize(eventData));rejects(proof);File.WriteAllBytes(file,bytes);}
   string marker=Path.Combine(root,"operations",opened,"writer.lock");File.WriteAllText(marker,"interrupted");rejects(proof);File.Delete(marker);
   string extraEvent=Path.Combine(root,"operations",opened,"000003.json");File.WriteAllText(extraEvent,"{}");rejects(proof);File.Delete(extraEvent);
   byte[] claimBytes=File.ReadAllBytes(claimPath);var badClaim=Clone(Program.Json.Deserialize<Dictionary<string,object>>(System.Text.Encoding.UTF8.GetString(claimBytes)));badClaim["copyId"]=new string('0',32);File.WriteAllText(claimPath,Program.Json.Serialize(badClaim));rejects(proof);File.WriteAllBytes(claimPath,claimBytes);
   string pending=Path.Combine(root,"requests",opened+".json.processing");File.WriteAllText(pending,"{}");rejects(proof);File.Delete(pending);
   var wrongChoice=Clone(args);wrongChoice["modelId"]=11;rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),wrongChoice,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));
   string fence=Path.Combine(Path.GetDirectoryName(working),"mutation-fence.json");File.WriteAllText(fence,"{}");rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));File.Delete(fence);
   string exportFence=Path.Combine(Path.GetDirectoryName(working),"geometry-export-fence.json");Directory.CreateDirectory(exportFence);rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));Directory.Delete(exportFence);
   byte[] sourceBytes=File.ReadAllBytes(source);File.WriteAllText(source,"changed source");rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));File.WriteAllBytes(source,sourceBytes);
   File.WriteAllText(working,"changed working");rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));File.WriteAllBytes(working,sourceBytes);
   var changed=Clone(listing);changed["windowCount"]=3;rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=changed,Snapshot=snapshot},pid=>false));
   NativeJournal.Freeze(Path.Combine(root,"openadoptions",opened+".once.json"),new{synthetic=true});rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),args,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false));
   var restore=Clone(args);restore["initialAdoptionId"]=observed["adoptionId"];restore["initialAdoptionSha256"]=observed["adoptionSha256"];OwnedOpenAdoption.Validate(restore,true);count++;rejects(()=>OwnedOpenAdoption.Validate(restore));rejects(()=>OwnedOpenAdoption.Adopt(root,Guid.NewGuid().ToString("N"),restore,()=>new ReadContext{Listing=listing,Snapshot=snapshot},pid=>false,true));
   check(originals.All(p=>Program.Hash(p.Key)==p.Value)&&Program.Hash(claimPath)==claimSha);return count;
  }
 }
}
