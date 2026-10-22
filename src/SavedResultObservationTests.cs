using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class SavedResultObservationTests {
  static Dictionary<string,object> Clone(Dictionary<string,object> d){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(d));}
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".saved-result-observation-fixture")))throw new InvalidOperationException("Isolated own fixture marker required; no CAD.");
   int count=0;Action<bool> need=v=>{if(!v)throw new Exception("Saved-result observation check failed at "+count);count++;};
   foreach(string fault in new[]{"control","different_other_windows","caller_alias","wrong_source_sha","changed_result_bytes","replaced_result","old_fence","changed_model","changed_process","changed_units","selection","wrong_active_choice","changed_context","late_windows","late_geometry","late_saved_bytes","expired","stale_listing","extra_argument","malformed_id"}){
    var f=new OwnedSavedReopenTests.Fixture();string root=f.Save.Fixture.Root,id=Guid.NewGuid().ToString("N");
    var c=f.Save.Context();
    var evidence=(System.Collections.IList)f.Verified["evidence"];string inspection=(string)((Dictionary<string,object>)evidence[1])["requestId"],listingId=(string)((Dictionary<string,object>)evidence[2])["requestId"];
    c.Snapshot=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",inspection+".json")));c.Listing=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"responses",listingId+".json")));
    // The existing SaveAs fixture declares its source at UtcNow + 1ms.
    // Declare the next observation after that source even on a coarse clock;
    // production chronology checks stay unchanged.
    DateTime observedAt=DateTime.UtcNow,sourceAt=EntitySets.Expiry(c.Snapshot["timestampUtc"]);if(observedAt<=sourceAt)observedAt=sourceAt.AddMilliseconds(1);
    c.Snapshot["timestampUtc"]=observedAt.ToString("o");c.Listing["timestampUtc"]=observedAt.ToString("o");
    if(fault=="different_other_windows"){
     string other=Path.Combine(root,"Other.psmodel");File.WriteAllText(other,"Own independent saved bytes");var w=Clone((Dictionary<string,object>)c.Listing["activeWindow"]);w["windowNumber"]=2;w["nativeWindowId"]=77;w["modelId"]=88;w["modelPath"]=other;w["modelName"]="Other";w["selectedRaw"]=0;
     c.Listing["windows"]=new[]{Clone((Dictionary<string,object>)c.Listing["activeWindow"]),w};c.Listing["windowCount"]=2;
    }
    if(fault=="changed_model")((Dictionary<string,object>)c.Snapshot["model"])["id"]=77;
    if(fault=="changed_process")c.Snapshot["processId"]=77;
    if(fault=="changed_units")c.Snapshot["lengthUnitFactorRaw"]=2;
    if(fault=="selection")c.Snapshot["selectionAfter"]=new[]{5};
    if(fault=="changed_context")c.Changed=true;
    if(fault=="late_windows")c.DuringRead=n=>{if(n==2)c.Listing["windowCount"]=2;};
    if(fault=="late_geometry")c.DuringRead=n=>{if(n==2)c.Snapshot["lengthUnitFactorRaw"]=2;};
    if(fault=="late_saved_bytes")c.DuringRead=n=>{if(n==2)File.AppendAllText(f.Save.ResultPath," synthetic late saved write");};
    if(fault=="stale_listing")c.Listing["timestampUtc"]=DateTime.UtcNow.AddMinutes(-6).ToString("o");
    string freshInspection=Guid.NewGuid().ToString("N"),freshListing=Guid.NewGuid().ToString("N");
    var args=new Dictionary<string,object>(f.Args){{"inspectionRequestId",freshInspection},{"inspectionSha256",NativeJournal.Freeze(Path.Combine(root,"responses",freshInspection+".json"),c.Snapshot)},{"listingRequestId",freshListing},{"listingSha256",NativeJournal.Freeze(Path.Combine(root,"responses",freshListing+".json"),c.Listing)},{"modelId",((Dictionary<string,object>)c.Snapshot["model"])["id"]},{"windowNumber",1},{"expiresUtc",DateTime.UtcNow.AddSeconds(240).ToString("o")}};
    if(fault=="wrong_source_sha")args["verificationReceiptSha256"]=new string('f',64);
    if(fault=="changed_result_bytes")File.AppendAllText(f.Save.ResultPath," changed result");
    if(fault=="replaced_result"){byte[] b=File.ReadAllBytes(f.Save.ResultPath);File.Move(f.Save.ResultPath,Path.Combine(Path.GetDirectoryName(f.Save.ResultPath),"old.psmodel"));File.WriteAllBytes(f.Save.ResultPath,b);}
    if(fault=="old_fence")File.WriteAllText(f.Save.Fence,"Unresolved own fixture fence");
    if(fault=="wrong_active_choice")args["windowNumber"]=2;
    if(fault=="expired")args["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    if(fault=="extra_argument")args["path"]=f.Save.ResultPath;
    if(fault=="malformed_id")args["inspectionRequestId"]="../../escape";
    var submitted=Clone(args);if(fault=="caller_alias")c.DuringRead=n=>{if(n==1){args["modelId"]=999;args["verificationReceiptSha256"]=new string('f',64);}};
    var protectedProofs=Directory.GetFiles(Path.Combine(root,"operations"),("*.json"),SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"responses"),"*.json")).ToDictionary(p=>p,p=>Program.Hash(p));bool control=fault=="control"||fault=="different_other_windows"||fault=="caller_alias",denied=false;
    try{var r=SavedResultObservation.Observe(root,id,args,c);need(control&&Equals(r["nativeProviderQualified"],false)&&Equals(r["executionAuthority"],false)&&Equals(r["sdkLeaseIssued"],false));if(fault=="caller_alias")need(EntitySets.Canonical(((Dictionary<string,object>)r["observation"])["parameters"])==EntitySets.Canonical(submitted));}catch(Exception error){if(!(error is IOException||error is InvalidDataException||error is ArgumentException||error is InvalidOperationException||error is UnauthorizedAccessException))throw;denied=true;if(control)throw new Exception("Control failed: "+fault,error);need(!control);}
    need(control?!denied:denied);need(c.Calls==0&&f.Save.Protected()&&protectedProofs.All(p=>Program.Hash(p.Key)==p.Value));
    if(!control)need(!File.Exists(Path.Combine(root,"savedresultobservations",id+".json")));
   }
   return count;
  }
 }
}
