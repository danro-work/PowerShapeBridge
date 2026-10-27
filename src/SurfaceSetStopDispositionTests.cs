using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 internal static class SurfaceSetStopDispositionTests {
  static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
  static Dictionary<string,object> Clone(object value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  // Promoted receipts are synthetic ONLY: fake contexts cannot take the release branch.
  static Dictionary<string,object> Setup(out SurfaceSetPartialStopTests.Fixture f,out string id){
   f=new SurfaceSetPartialStopTests.Fixture();var o=f.Read();Need(Equals(o["state"],"surface_set_partial_first_step_observed"),Program.Json.Serialize(o));o["nativeProviderQualified"]=true;string oid=Guid.NewGuid().ToString("N");o["bridgeRequestId"]=oid;var a=Clone(f.Args);a["observationRequestId"]=oid;a["observationReceiptSha256"]=NativeJournal.Freeze(Path.Combine(f.F.Root,"responses",oid+".json"),o);id=Guid.NewGuid().ToString("N");return a;
  }
  internal static int Run(){
   Need(File.Exists(Path.Combine(Program.Root,".surface-set-stop-disposition-fixture")),"Isolated disposition fixture marker required; no COM/CAD.");int cases=0;object control=null;
   foreach(string fault in new[]{"control","unqualified_observer","wrong_observer_sha","changed_capture","expired","existing_archive","missing_claim","tampered_archive","stale_verify","missing_fence"}){
    SurfaceSetPartialStopTests.Fixture f;string pid;var a=Setup(out f,out pid);string root=f.F.Root,copy=(string)f.Execution["copyId"],fence=OwnedTranslation.FencePath(root,copy);var original=new Dictionary<string,string>();using(var held=new OwnedOperationPlans.Proofs()){var history=new SurfaceSetPartialStop.Origin(root,f.Id,f.Sha,held);foreach(var pin in history.Pins)original.Add(pin.Key,pin.Value);}
    if(fault=="unqualified_observer"){string oid=Guid.NewGuid().ToString("N");var o=f.Read();a["observationRequestId"]=oid;a["observationReceiptSha256"]=NativeJournal.Freeze(Path.Combine(root,"responses",oid+".json"),o);}
    if(fault=="wrong_observer_sha")a["observationReceiptSha256"]=new string('0',64);
    if(fault=="changed_capture")f.Context.Fault="other_drift";
    if(fault=="expired")a["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    var p=SurfaceSetStopDisposition.Prepare(root,pid,a,f.Context);bool rejectPrep=new[]{"unqualified_observer","wrong_observer_sha","changed_capture","expired"}.Contains(fault);
    Need(Equals(p["state"],rejectPrep?"surface_set_stop_disposition_preparation_rejected":"surface_set_stop_disposition_prepared"),fault+": "+Program.Json.Serialize(p));
    if(!rejectPrep){
     Need(Equals(p["nativeProviderQualified"],false)&&Equals(p["copyFenceReleased"],false),"Fake preparation qualified.");p["nativeProviderQualified"]=true;p["bridgeRequestId"]=pid;
     var refs=new Dictionary<string,object>{{"dispositionId",pid},{"dispositionSha256",p["dispositionSha256"]},{"preparationReceiptSha256",NativeJournal.Freeze(Path.Combine(root,"responses",pid+".json"),p)}};
     string archive=SurfaceSetStopDisposition.Archive(root,f.Id);if(fault=="existing_archive")File.WriteAllText(archive,"unrelated");string xid=Guid.NewGuid().ToString("N");var x=SurfaceSetStopDisposition.Execute(root,xid,refs,f.Context);
     Need(Equals(x["state"],fault=="existing_archive"?"surface_set_stop_disposition_rejected_before_change":"surface_set_stop_disposition_executed_unverified"),fault+": "+Program.Json.Serialize(x));
     if(fault!="existing_archive"){
      Need(Program.Hash(archive)==original[fence]&&File.Exists(fence)&&File.Exists(SurfaceSetStopDisposition.Secondary(root,copy))&&Equals(x["nativeProviderQualified"],false),"Archive lost quarantine or fake provider qualified.");
      Need(Equals(SurfaceSetStopDisposition.Execute(root,Guid.NewGuid().ToString("N"),refs,f.Context)["state"],"surface_set_stop_disposition_rejected_before_change"),"Consumed archive replay admitted.");cases++;
      x["nativeProviderQualified"]=true;x["bridgeRequestId"]=xid;string xsha=NativeJournal.Freeze(Path.Combine(root,"responses",xid+".json"),x);f.Refresh();var va=Clone(f.Args);va["executionRequestId"]=xid;va["executionReceiptSha256"]=xsha;
      if(fault=="missing_claim")File.Delete(Path.Combine(root,"surfacesetstopdispositions",pid+".execution-once.json"));
      if(fault=="tampered_archive")File.AppendAllText(archive," ");
      if(fault=="missing_fence")File.AppendAllText(fence," ");
      if(fault=="stale_verify")va=Clone(a);va.Remove("observationRequestId");va.Remove("observationReceiptSha256");
      if(fault=="stale_verify"){va["executionRequestId"]=xid;va["executionReceiptSha256"]=xsha;}
      string vid=Guid.NewGuid().ToString("N");var v=SurfaceSetStopDisposition.Verify(root,vid,va,f.Context);Need(Equals(v["state"],fault=="control"?"surface_set_stop_disposition_samples_match_unqualified":"surface_set_stop_disposition_verification_rejected"),fault+": "+Program.Json.Serialize(v));if(fault=="control"){v["bridgeRequestId"]=vid;string vsha=NativeJournal.Freeze(Path.Combine(root,"responses",vid+".json"),v);control=new{root,originalExecutionId=f.Id,originalExecutionSha256=f.Sha,preparationReferences=refs,executionId=xid,executionSha256=xsha,verificationId=vid,verificationSha256=vsha,verificationArguments=va};}Need(Equals(v["copyFenceReleased"],false)&&Equals(v["mutationAuthorityGranted"],false)&&File.Exists(fence)&&File.Exists(SurfaceSetStopDisposition.Secondary(root,copy)),"Fake or failed verification released quarantine.");
     }
    }
    foreach(var pin in original)if(!(fault=="missing_fence"&&pin.Key==fence))Need(File.Exists(pin.Key)&&Program.Hash(pin.Key)==pin.Value,"Original history changed: "+pin.Key);
    Need(f.Context.Calls==1,"Disposition repeated a CAD callback.");cases++;
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","surface-set-stop-disposition-tests.json"),new{state="passed",cases,cadConnectionAttempted=false,originalCADCallsPerFixture=1,dispositionCADCalls=0,liveFenceReleased=false,fixture=control});Console.WriteLine("PASS: "+cases+" disposition/archive/claim/replay/current-context/fake-provider quarantine guards; no CAD.");return cases;
  }
 }
}
