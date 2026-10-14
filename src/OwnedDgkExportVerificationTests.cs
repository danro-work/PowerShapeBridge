using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedDgkExportVerificationTests {
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,object> Read(string path){return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Context : OwnedDgkExportVerification.IContext {
   internal Dictionary<string,object> Snapshot,Listing,Settings;internal string Fault;internal int Reads;
   internal Context(OwnedDgkExportTests.Context previous){Snapshot=Clone(previous.Observation());Listing=Clone(previous.Listing);Settings=Clone(previous.Settings);}
   public Dictionary<string,object> Observation(){Reads++;var value=Clone(Snapshot);if(Fault=="late_context"&&Reads>=4)value["processId"]=99;return value;}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> ExportSettings(){var value=Clone(Settings);if(Fault=="settings")value["effectiveOptionsSha256"]=new string('f',64);return value;}
   public int Composite(int type,int id){return Fault=="mapping"?999:type*100000+id;}
   public int[][] SelectedKeys(){return Fault=="selected_keys"?new[]{new[]{40,5}}:new int[0][];}
   public bool Busy(){return Fault=="busy";}
   public void ConfirmDocument(){if(Fault=="retained_document")throw new InvalidOperationException("Independent retained fake document changed.");}
   public void Dispose(){}
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-dgk-verification-fixture")),"Isolated DGK verifier marker required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   string[] faults={"control","mixed","all_companions","historical_expiry","receipt_sha","target_sha","summary_sha","step_sha","fence_sha","claim",
    "output_bytes","missing_output","replaced_output","stray_output","old_inspection","equal_inspection","old_listing","future_listing","expired_reads",
    "settings","mapping","selected_keys","busy","late_context","retained_document","wrong_window","foreign_model","unknown_export"};
   foreach(string fault in faults) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);fixture.Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","Synthetic"},{"activeRaw",1}}};
    var keys=fault=="mixed"?new[]{new[]{40,5},new[]{21,8},new[]{59,10}}:new[]{new[]{40,5}};
    var request=OwnedGeometryExportTargetTests.Rebind(fixture,fixture.Snapshot,fault=="historical_expiry"?(DateTime?)DateTime.UtcNow.AddSeconds(4):null,keys);
    var parameters=new Dictionary<string,object>{{"coordinateFrame","world"},{"exportVariant","geometry"},{"triangleCompanions",new object[0]},{"featureCompanions",new object[0]},{"settingsQualification","planned_unqualified"}};
    if(fault=="all_companions") {
     parameters["exportVariant"]="geometry_features";parameters["triangleCompanions"]=new[]{new Dictionary<string,object>{{"suffix",".dgk.dmt"},{"required",true}}};
     parameters["featureCompanions"]=new[]{new Dictionary<string,object>{{"name","explicit_features.xml"},{"role","features_xml"},{"required",true}}};
    }
    request["parameters"]=parameters;string targetId=Guid.NewGuid().ToString("N"),executionId=Guid.NewGuid().ToString("N");
    var target=OwnedDgkTargets.Prepare(fixture.Root,targetId,request);Require(Equals(target["state"],"owned_dgk_target_prepared_files"),"Verifier target setup failed.");
    var executionContext=new OwnedDgkExportTests.Context(fixture,Clone(parameters)){Fault=fault=="unknown_export"?"lost_reply":null};
    executionContext.Primary=(string)((Dictionary<string,object>)target["target"])["targetPath"];
    var executed=OwnedDgkExport.Execute(fixture.Root,executionId,new Dictionary<string,object>{{"targetId",targetId},{"targetSha256",target["targetSha256"]}},executionContext);
    Require(Equals(executed["state"],fault=="unknown_export"?"owned_dgk_export_outcome_unknown":"owned_dgk_export_executed_unverified"),"Verifier execution setup failed: "+Program.Json.Serialize(executed));
    string receiptSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",executionId+".json"),executed);
    DateTime returned=fault=="unknown_export"?EntitySets.Expiry(executed["timestampUtc"]):NativeJournal.ForVerification(fixture.Root,executionId,(string)executed["planSha256"]).CommandReturnedUtc;
    OwnedRotationVerificationTests.WaitAfter(returned);
    if(fault=="historical_expiry") {
     DateTime expired=EntitySets.Expiry(((Dictionary<string,object>)target["target"])["expiresUtc"]);
     Require(expired<=DateTime.UtcNow.AddSeconds(5),"Fixture wait unexpectedly long.");
     while(DateTime.UtcNow<=expired)System.Threading.Thread.Sleep(25);
    }
    var context=new Context(executionContext){Fault=fault};var inspection=Clone(context.Snapshot);
    inspection["timestampUtc"]=fault=="old_inspection"?fixture.Snapshot["timestampUtc"]:fault=="equal_inspection"?returned.ToString("o"):DateTime.UtcNow.ToString("o");
    string inspectionId=Guid.NewGuid().ToString("N");inspection["bridgeRequestId"]=inspectionId;
    if(fault=="foreign_model")((Dictionary<string,object>)inspection["model"])["id"]=99;
    string inspectionSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",inspectionId+".json"),inspection);
    var listing=Clone(context.Listing);listing["timestampUtc"]=fault=="old_listing"?returned.ToString("o"):fault=="future_listing"?DateTime.UtcNow.AddSeconds(10).ToString("o"):DateTime.UtcNow.ToString("o");listing["state"]="documents_listed_verified";
    string listingId=Guid.NewGuid().ToString("N"),listingSha=NativeJournal.Freeze(Path.Combine(fixture.Root,"responses",listingId+".json"),listing);
    var args=new Dictionary<string,object>{{"executionRequestId",executionId},{"executionReceiptSha256",receiptSha},{"inspectionRequestId",inspectionId},{"inspectionSha256",inspectionSha},
     {"listingRequestId",listingId},{"listingSha256",listingSha},{"windowNumber",fault=="wrong_window"?99:1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(fault=="expired_reads"?-1:150).ToString("o")}};
    string folder=OwnedDgkTargets.Folder(fixture.Root,targetId),output=executionContext.Primary;
    if(fault=="receipt_sha")args["executionReceiptSha256"]=new string('f',64);
    if(fault=="target_sha")File.AppendAllText(Path.Combine(folder,"target.json")," ");
    if(fault=="summary_sha")File.AppendAllText(Path.Combine(folder,"export-summary.json")," ");
    if(fault=="step_sha")File.AppendAllText(Path.Combine(folder,"000000.returned.json")," ");
    if(fault=="fence_sha")File.AppendAllText(OwnedGeometryExportTargets.ExportFencePath(fixture.Root,(string)executed["copyId"])," ");
    if(fault=="claim") {var claim=Read(Path.Combine(folder,"export-once.json"));claim["operationId"]=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(folder,"export-once.json"),Program.Json.Serialize(claim));}
    if(fault=="output_bytes")File.AppendAllText(output,"changed");
    if(fault=="missing_output")File.Move(output,Path.Combine(fixture.Root,"retained.dgk"));
    if(fault=="replaced_output") {File.Move(output,Path.Combine(fixture.Root,"retained.dgk"));File.Copy(Path.Combine(fixture.Root,"retained.dgk"),output);}
    if(fault=="stray_output")File.WriteAllText(Path.Combine(Path.GetDirectoryName(output),"stray.xml"),"Undeclared.");
    var before=Hashes(fixture.Root);string verificationId=Guid.NewGuid().ToString("N");var result=OwnedDgkExportVerification.Verify(fixture.Root,verificationId,args,context);
    bool control=new[]{"control","mixed","all_companions","historical_expiry"}.Contains(fault);
    Require(Equals(result["state"],control?"observed_dgk_bundle_context_match_unqualified":"owned_dgk_export_verification_rejected"),fault+": "+Program.Json.Serialize(result));
    Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Independent DGK verification changed protected files/journal/fence.");
    foreach(string flag in new[]{"exportAttempted","contextChangeAttempted","geometryMutationAttempted","cadSaveAttempted","geometryVerified","nativeProviderQualified","copyFenceReleased","retryAllowed"})Require(Equals(result[flag],false),"Independent verifier acquired mutation authority.");
    Require(executionContext.Adds==keys.Length&&executionContext.Exports==1&&executionContext.Clears==(fault=="unknown_export"?0:1),"Independent verifier repeated a callback.");
    if(control)using(var historical=new OwnedDgkTargets.TargetLease(fixture.Root,targetId,(string)target["targetSha256"],true)) {
     bool rejected=false;try{historical.Recheck();}catch(InvalidDataException){rejected=true;}Require(rejected,"Historical DGK lease authorized execution.");
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",fixture.Root},{"targetId",targetId},{"executionId",executionId},{"verificationId",verificationId},
     {"parameters",args},{"result",result},{"verificationReads",context.Reads},{"protectedHashes",before},{"artifactHashes",Hashes(fixture.Root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-dgk-verification-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" independent DGK verification cases; no CAD/fence release.");return rows.Count;
  }
 }
}
