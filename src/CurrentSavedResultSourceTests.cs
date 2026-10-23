using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class CurrentSavedResultSourceTests {
  static Dictionary<string,object> Clone(Dictionary<string,object> d){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(d));}
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".current-saved-source-fixture")))throw new InvalidOperationException("Isolated own current-source fixture marker required; no CAD.");
   int checks=0;Action<bool> need=ok=>{if(!ok)throw new Exception("Current saved source check failed at "+checks);checks++;};
   foreach(string fault in new[]{"control","unqualified","raw_body","wrong_response_sha","wrong_report_sha","self_reference","different_model","different_process","different_units","changed_digest","wrong_window","excess_lifetime","changed_result","fence","extra_argument","lease_pin","late_result_write","late_expiry","expired_admission","historical_evidence"}){
    var f=new OwnedSavedReopenTests.Fixture();string root=f.Save.Fixture.Root,id=Guid.NewGuid().ToString("N"),passiveId=Guid.NewGuid().ToString("N");
    var evidence=(IList)f.Verified["evidence"];var context=f.Save.Context();
    context.Snapshot=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",(string)((Dictionary<string,object>)evidence[1])["requestId"]+".json")));
    context.Listing=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",(string)((Dictionary<string,object>)evidence[2])["requestId"]+".json")));
    DateTime originalAt=EntitySets.Expiry(context.Snapshot["timestampUtc"]);if(!System.Threading.SpinWait.SpinUntil(()=>DateTime.UtcNow>=originalAt,1000))throw new Exception("Synthetic chronology wait failed.");
    context.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");context.Listing["timestampUtc"]=DateTime.UtcNow.ToString("o");
    string inspect=Guid.NewGuid().ToString("N"),listing=Guid.NewGuid().ToString("N");
    var parameters=new Dictionary<string,object>(f.Args){{"inspectionRequestId",inspect},{"inspectionSha256",NativeJournal.Freeze(Path.Combine(root,"responses",inspect+".json"),context.Snapshot)},{"listingRequestId",listing},{"listingSha256",NativeJournal.Freeze(Path.Combine(root,"responses",listing+".json"),context.Listing)},{"windowNumber",1},{"modelId",((Dictionary<string,object>)context.Snapshot["model"])["id"]},{"expiresUtc",DateTime.UtcNow.AddSeconds(200).ToString("o")}};
    var passive=SavedResultObservation.Observe(root,passiveId,parameters,context);need(Equals(passive["nativeProviderQualified"],false));
    // Explicitly forged isolated parser fixture, never a native qualification.
    // The real core still refuses to qualify Fake contexts.
    var report=Clone((Dictionary<string,object>)passive["observation"]);report["observationId"]=id;report["nativeProviderQualified"]=true;report["state"]="current_saved_result_observed";
    if(fault=="unqualified")report["nativeProviderQualified"]=false;
    if(fault=="self_reference"){report["originalVerificationRequestId"]=id;((Dictionary<string,object>)report["parameters"])["verificationRequestId"]=id;}
    if(fault=="different_model")((Dictionary<string,object>)report["model"])["id"]=99;
    if(fault=="different_process"||fault=="different_units"){
     var changed=Clone(context.Snapshot);changed[fault=="different_process"?"processId":"lengthUnitFactorRaw"]=99;
     string altered=Guid.NewGuid().ToString("N");var p=(Dictionary<string,object>)report["parameters"];p["inspectionRequestId"]=altered;p["inspectionSha256"]=NativeJournal.Freeze(Path.Combine(root,"responses",altered+".json"),changed);report["observedPropertiesSha256"]=EntitySets.ObservedDigest(changed);
    }
    if(fault=="changed_digest")report["observedPropertiesSha256"]=new string('f',64);
    if(fault=="wrong_window")((Dictionary<string,object>)report["parameters"])["windowNumber"]=2;
    if(fault=="excess_lifetime"){string future=DateTime.UtcNow.AddHours(1).ToString("o");report["expiresUtc"]=future;((Dictionary<string,object>)report["parameters"])["expiresUtc"]=future;}
    string reportSha=NativeJournal.Freeze(Path.Combine(root,"savedresultobservations",id+".json"),report);
    var raw=new Dictionary<string,object>{{"bridgeRequestId",id},{"observationId",id},{"state","current_saved_result_observed"},{"observationSha256",reportSha},{"observation",Clone(report)},{"nativeProviderQualified",true},{"executionAuthority",false},{"sdkLeaseIssued",false}};
    if(fault=="raw_body")((Dictionary<string,object>)raw["observation"])["fullTopologyEquivalent"]=true;
    if(fault=="wrong_report_sha")raw["observationSha256"]=new string('f',64);
    var args=new Dictionary<string,object>{{"observationRequestId",id},{"observationReceiptSha256",NativeJournal.Freeze(Path.Combine(root,"responses",id+".json"),raw)}};
    if(fault=="wrong_response_sha")args["observationReceiptSha256"]=new string('f',64);
    if(fault=="changed_result")File.AppendAllText(f.Save.ResultPath," changed saved bytes");
    if(fault=="fence")File.WriteAllText(f.Save.Fence,"Unresolved synthetic fence");
    if(fault=="extra_argument")args["historicalProof"]=true;
    var protectedFiles=Directory.GetFiles(Path.Combine(root,"operations"),"*.json",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"responses"),"*.json")).ToDictionary(p=>p,p=>Program.Hash(p));
    bool allowed=new[]{"control","lease_pin","late_result_write","late_expiry","historical_evidence"}.Contains(fault),rejected=false;DateTime now=DateTime.UtcNow;if(fault=="expired_admission"||fault=="historical_evidence")now=now.AddMinutes(6);
    try{using(var source=new CurrentSavedResultSource(root,args,fault=="historical_evidence",()=>now)){
     need(allowed&&source.Expires==EntitySets.Expiry(parameters["expiresUtc"])&&Equals(source.Report["executionAuthority"],false)&&Equals(source.Original.Verified["state"],"owned_save_as_verified_observed"));source.Recheck();
     if(fault=="lease_pin"){bool locked=false;try{File.AppendAllText(Path.Combine(root,"savedresultobservations",id+".json")," overwrite");}catch(IOException){locked=true;}need(locked);source.Recheck();}
     if(fault=="late_result_write"||fault=="late_expiry"){
      if(fault=="late_result_write")File.AppendAllText(f.Save.ResultPath," synthetic later write");else now=now.AddMinutes(6);
      bool refused=false;try{source.Recheck();}catch(IOException){refused=true;}catch(InvalidOperationException){refused=true;}need(refused);
     }
    }}catch(Exception e){if(!(e is IOException||e is InvalidDataException||e is InvalidOperationException||e is UnauthorizedAccessException||e is ArgumentException))throw;if(allowed)throw new Exception("Control failed: "+fault,e);rejected=true;}
    need(allowed?!rejected:rejected);need(context.Calls==0&&f.Save.Protected()&&protectedFiles.All(p=>Program.Hash(p.Key)==p.Value));
   }
   return checks;
  }
 }
}
