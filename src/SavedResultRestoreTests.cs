using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class SavedResultRestoreTests {
  static Dictionary<string,object> Clone(Dictionary<string,object> d){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(d));}
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".saved-result-restore-fixture")))throw new InvalidOperationException("Own restoration fixture marker required; no CAD.");
   int checks=0;Action<bool> need=ok=>{if(!ok)throw new Exception("Saved restoration wire check failed at "+checks);checks++;};
   Action<Action> rejects=action=>{bool denied=false;try{action();}catch(Exception e){if(!(e is IOException||e is InvalidDataException||e is ArgumentException||e is InvalidOperationException||e is UnauthorizedAccessException))throw;denied=true;}need(denied);};
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));need(Program.ParseConfig(Program.Json.Serialize(config)).ownedSavedResultRestoreExperimental);
   foreach(object bad in new object[]{"true",1,null}){var changed=Clone(config);changed["ownedSavedResultRestoreExperimental"]=bad;rejects(()=>Program.ParseConfig(Program.Json.Serialize(changed)));}
   foreach(string key in new[]{"ownedSavedReopenExperimental","ownedSavesExperimental","ownedSaveTargetsExperimental","workingCopyOpenExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"}){var changed=Clone(config);changed[key]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(changed)));}
   var f=new OwnedSavedReopenTests.Fixture();string root=f.Save.Fixture.Root;
   var evidence=(IList)f.Verified["evidence"];var context=f.Save.Context();context.Snapshot=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",(string)((Dictionary<string,object>)evidence[1])["requestId"]+".json")));context.Listing=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",(string)((Dictionary<string,object>)evidence[2])["requestId"]+".json")));
   DateTime sourceAt=EntitySets.Expiry(context.Snapshot["timestampUtc"]);if(!System.Threading.SpinWait.SpinUntil(()=>DateTime.UtcNow>=sourceAt,1000))throw new Exception("Synthetic chronology unavailable.");context.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");context.Listing["timestampUtc"]=DateTime.UtcNow.ToString("o");
   string inspect=Guid.NewGuid().ToString("N"),listing=Guid.NewGuid().ToString("N"),passiveId=Guid.NewGuid().ToString("N"),id=Guid.NewGuid().ToString("N");
   var parameters=new Dictionary<string,object>(f.Args){{"inspectionRequestId",inspect},{"inspectionSha256",NativeJournal.Freeze(Path.Combine(root,"responses",inspect+".json"),context.Snapshot)},{"listingRequestId",listing},{"listingSha256",NativeJournal.Freeze(Path.Combine(root,"responses",listing+".json"),context.Listing)},{"modelId",((Dictionary<string,object>)context.Snapshot["model"])["id"]},{"windowNumber",1},{"expiresUtc",DateTime.UtcNow.AddSeconds(120).ToString("o")}};
   var passive=SavedResultObservation.Observe(root,passiveId,parameters,context);need(Equals(passive["nativeProviderQualified"],false));
   // A forged qualified parser fixture only. No Fake context becomes qualified.
   var report=Clone((Dictionary<string,object>)passive["observation"]);report["observationId"]=id;report["nativeProviderQualified"]=true;report["state"]="current_saved_result_observed";
   string sha=NativeJournal.Freeze(Path.Combine(root,"savedresultobservations",id+".json"),report);var raw=new Dictionary<string,object>{{"state","current_saved_result_observed"},{"observationId",id},{"observationSha256",sha},{"observation",report},{"nativeProviderQualified",true},{"executionAuthority",false},{"sdkLeaseIssued",false}};
   var args=new Dictionary<string,object>{{"observationRequestId",id},{"observationReceiptSha256",NativeJournal.Freeze(Path.Combine(root,"responses",id+".json"),raw)}};
   var protectedProofs=Directory.GetFiles(Path.Combine(root,"operations"),"*.json",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"responses"),"*.json")).ToDictionary(p=>p,p=>Program.Hash(p));
   rejects(()=>SavedReopenSource.Validate(args));rejects(()=>OwnedSavedReopen.Prepare(root,Guid.NewGuid().ToString("N"),args));
   string reopen=Guid.NewGuid().ToString("N");var prepared=OwnedSavedReopen.Prepare(root,reopen,args,null,true);var plan=(Dictionary<string,object>)prepared["reopen"];
   need(Equals(plan["schemaVersion"],2)&&plan.Count==21&&!plan.ContainsKey("verificationRequestId")&&Equals(plan["observationRequestId"],id));
   using(var lease=new OwnedSavedReopenLease(root,new Dictionary<string,object>{{"reopenId",reopen},{"reopenSha256",prepared["reopenSha256"]}})){lease.Recheck();need(lease.Source.Current&&Equals(lease.Source.Verified["state"],"current_saved_result_observed")&&EntitySets.Canonical(lease.Source.Snapshot)==EntitySets.Canonical(context.Snapshot));}
   foreach(string key in new[]{"workingPath","checkpointPath"})need(Program.Hash((string)plan[key])==f.ResultSha);
   var legacy=OwnedSavedReopen.Prepare(root,Guid.NewGuid().ToString("N"),f.Args);var legacyPlan=(Dictionary<string,object>)legacy["reopen"];need(Equals(legacyPlan["schemaVersion"],1)&&legacyPlan.ContainsKey("verificationRequestId")&&!legacyPlan.ContainsKey("observationRequestId"));
   using(var lease=new OwnedSavedReopenLease(root,new Dictionary<string,object>{{"reopenId",legacy["reopenId"]},{"reopenSha256",legacy["reopenSha256"]}})){need(!lease.Source.Current);lease.Recheck();}
   need(protectedProofs.All(p=>Program.Hash(p.Key)==p.Value)&&f.Save.Protected()&&context.Calls==0&&!File.Exists(Path.Combine(root,"workingcopies",reopen,"open-once.json")));
   return checks;
  }
 }
}
