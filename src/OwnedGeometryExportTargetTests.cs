using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedGeometryExportTargetTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Read(string path){return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Program.Hash(p));}
  static void Reject(Action action){bool denied=false;try{action();}catch(InvalidDataException){denied=true;}catch(IOException){denied=true;}catch(InvalidOperationException){denied=true;}catch(ArgumentException){denied=true;}Require(denied,"Expected lease rejection.");}
  static Dictionary<string,object> Args(OwnedOperationPlanTests.Fixture f){return new Dictionary<string,object>{{"bindingId",f.BindingId},{"bindingSha256",f.Parameters["bindingSha256"]},{"entitySetId",f.SetId},{"entitySetSha256",Program.Hash(Path.Combine(f.Root,"entitysets",f.SetId+".json"))}};}
  internal static Dictionary<string,object> Rebind(OwnedOperationPlanTests.Fixture f,Dictionary<string,object> snapshot,DateTime? expiry=null,int[][] keys=null) {
   string sourceId=Guid.NewGuid().ToString("N"),bindingId=Guid.NewGuid().ToString("N"),setId=Guid.NewGuid().ToString("N");snapshot["bridgeRequestId"]=sourceId;
   string sha=NativeJournal.Freeze(Path.Combine(f.Root,"responses",sourceId+".json"),snapshot);var old=Read(Path.Combine(f.Root,"documentbindings",f.BindingId+".json"));var context=f.NewContext();context.Snapshot=snapshot;string expires=(expiry??DateTime.UtcNow.AddSeconds(180)).ToString("o");
   var binding=DocumentBindings.Prepare(f.Root,bindingId,new Dictionary<string,object>{{"copyId",old["copyId"]},{"ownershipSha256",old["ownershipSha256"]},{"inspectionRequestId",sourceId},{"inspectionSha256",sha},{"expiresUtc",expires}},snapshot,context,new Dictionary<string,object>());
   string setSha=NativeJournal.Freeze(Path.Combine(f.Root,"entitysets",setId+".json"),new Dictionary<string,object>{{"inspectionRequestId",sourceId},{"inspectionSha256",sha},{"expiresUtc",expires},{"entityKeys",keys??new[]{new[]{40,5}}}});
   return new Dictionary<string,object>{{"bindingId",bindingId},{"bindingSha256",binding["bindingSha256"]},{"entitySetId",setId},{"entitySetSha256",setSha}};
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".geometry-export-fixture")),"Explicit isolated export marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   string[] faults={"control","mixed_mesh","rotation_returned","summary_tamper","returned_tamper","claim_tamper","unknown_fence","partial_rotation","old_after_rotation","working_byte_capture","late_working","checkpoint_writable","wrong_binding_sha","wrong_set_sha","extra_path","curve_target","expired","nonempty_selection","reserved","existing_folder","lease_control","output_file","output_directory","claimed","late_fence","wrong_target_path","wrong_keys","wrong_expiry","replaced_directory","tampered_target"};
   foreach(string fault in faults) {
    var f=new OwnedOperationPlanTests.Fixture(true,true);var args=Args(f);string id=Guid.NewGuid().ToString("N");string folder=OwnedGeometryExportTargets.Folder(f.Root,id);bool rotated=new[]{"rotation_returned","summary_tamper","returned_tamper","claim_tamper","partial_rotation","old_after_rotation"}.Contains(fault);
    if(rotated) {
     var rotation=new OwnedRotationPlanTests.Fixture(fault=="partial_rotation");f=rotation.Base;args=Args(f);folder=OwnedGeometryExportTargets.Folder(f.Root,id);rotation.Freeze();string packetId=Guid.NewGuid().ToString("N");var packet=OwnedRotationPlans.Prepare(f.Root,packetId,rotation.Args,f.NewContext());var context=new OwnedRotationVerificationTests.Context(rotation){Fault=fault=="partial_rotation"?"partial_second":null};string executionId=Guid.NewGuid().ToString("N");var executed=OwnedRotationExecution.Execute(f.Root,executionId,new Dictionary<string,object>{{"ownedRotationId",packetId},{"ownedRotationSha256",packet["ownedRotationSha256"]}},context);
     Require(Equals(executed["state"],fault=="partial_rotation"?"owned_rotation_outcome_unknown":"owned_rotation_executed_unverified"),"Synthetic rotation setup failed.");
     if(fault!="old_after_rotation") {OwnedRotationVerificationTests.WaitAfter(EntitySets.Expiry(executed["timestampUtc"]));context.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");args=Rebind(f,context.Snapshot);}
     if(fault=="summary_tamper")File.AppendAllText(Path.Combine(f.Root,"rotationcommands",executionId,"summary.json")," ");if(fault=="returned_tamper")File.AppendAllText(Path.Combine(f.Root,"rotationcommands",executionId,"000000.returned.json")," ");if(fault=="claim_tamper") {var path=Path.Combine(f.Root,"rotationplans",rotation.PlanId+".execution-once.json");var claim=Read(path);claim["copyId"]=Guid.NewGuid().ToString("N");File.WriteAllText(path,Program.Json.Serialize(claim));}
    }
    if(fault=="mixed_mesh"||fault=="curve_target")args=Rebind(f,f.NewContext().Snapshot,null,fault=="mixed_mesh"?new[]{new[]{40,5},new[]{21,8}}:new[]{new[]{59,10}});
    var binding=Read(Path.Combine(f.Root,"documentbindings",(string)args["bindingId"]+".json"));string fence=OwnedTranslation.FencePath(f.Root,(string)binding["copyId"]);
    if(fault=="unknown_fence")File.WriteAllText(fence,"Unresolved own synthetic command.");
    if(fault=="working_byte_capture")File.AppendAllText(f.Working,"Own bytes captured at preparation, not original checkpoint bytes.");
    if(fault=="checkpoint_writable") {var owner=Read(Path.Combine(Path.GetDirectoryName(f.Working),"ownership.json"));File.SetAttributes((string)((Dictionary<string,object>)owner["checkpoint"])["path"],FileAttributes.Normal);}
    if(fault=="wrong_binding_sha")args["bindingSha256"]=new string('f',64);
    if(fault=="wrong_set_sha")args["entitySetSha256"]=new string('f',64);
    if(fault=="extra_path")args["targetPath"]=f.Working;
    if(fault=="expired") {var set=Read(Path.Combine(f.Root,"entitysets",f.SetId+".json"));set["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");string setId=Guid.NewGuid().ToString("N");args["entitySetId"]=setId;args["entitySetSha256"]=NativeJournal.Freeze(Path.Combine(f.Root,"entitysets",setId+".json"),set);}
    if(fault=="nonempty_selection") {var snapshot=f.NewContext().Snapshot;snapshot["selectionBefore"]=new[]{40};snapshot["selectionAfter"]=new[]{40};args=Rebind(f,snapshot);}
    if(fault=="reserved") {Directory.CreateDirectory(Path.GetDirectoryName(folder));File.WriteAllText(folder+".reservation.json","Prior synthetic reservation.");}
    if(fault=="existing_folder")Directory.CreateDirectory(folder);
    var before=Hashes(f.Root);var result=OwnedGeometryExportTargets.Prepare(f.Root,id,args);
    bool prepared=new[]{"control","mixed_mesh","rotation_returned","working_byte_capture","late_working","lease_control","output_file","output_directory","claimed","late_fence","wrong_target_path","wrong_keys","wrong_expiry","replaced_directory","tampered_target"}.Contains(fault);
    Require(Equals(result["state"],prepared?"owned_geometry_export_target_prepared_files":"owned_geometry_export_target_rejected"),fault+": "+Program.Json.Serialize(result));Require(before.All(p=>Program.Hash(p.Key)==p.Value),"Protected input changed.");
    foreach(string flag in new[]{"executionEnabled","exportAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","retryAllowed"})Require(Equals(result[flag],false),"Unexpected CAD authority.");
    bool? leaseAccepted=null;
    if(prepared) {
     string sha=(string)result["targetSha256"];var plan=Read(Path.Combine(folder,"target.json"));string target=(string)plan["targetPath"];
     using(var lease=new OwnedGeometryExportTargets.TargetLease(f.Root,id,sha)) {lease.Recheck();Reject(()=>Directory.Move(folder,folder+"-moved"));Reject(()=>File.AppendAllText(Path.Combine(folder,"target.json"),"tamper"));}
     if(fault=="output_file")File.WriteAllText(target,"Opaque collision; not DMT.");if(fault=="output_directory")Directory.CreateDirectory(target);if(fault=="claimed")File.WriteAllText(Path.Combine(folder,"export-once.json"),"Prior own claim.");if(fault=="late_fence")File.WriteAllText(fence,"New unknown command.");if(fault=="late_working")File.AppendAllText(f.Working,"Changed after prepared byte capture.");
     if(new[]{"wrong_target_path","wrong_keys","wrong_expiry"}.Contains(fault)) {if(fault=="wrong_target_path")plan["targetPath"]=f.Working;if(fault=="wrong_keys")plan["entityKeys"]=new[]{new[]{40,999}};if(fault=="wrong_expiry")plan["expiresUtc"]=DateTime.UtcNow.AddSeconds(400).ToString("o");File.WriteAllText(Path.Combine(folder,"target.json"),Program.Json.Serialize(plan));sha=Program.Hash(Path.Combine(folder,"target.json"));}
     if(fault=="replaced_directory") {Directory.Move(folder,folder+"-original");Directory.CreateDirectory(folder);File.Copy(Path.Combine(folder+"-original","target.json"),Path.Combine(folder,"target.json"));}
     if(fault=="tampered_target")File.AppendAllText(Path.Combine(folder,"target.json")," ");
     leaseAccepted=new[]{"control","mixed_mesh","rotation_returned","working_byte_capture","lease_control"}.Contains(fault);
     if(leaseAccepted.Value)using(var lease=new OwnedGeometryExportTargets.TargetLease(f.Root+"\\",id,sha)){lease.Recheck();}else Reject(()=>{using(var lease=new OwnedGeometryExportTargets.TargetLease(f.Root,id,sha)){lease.Recheck();}});
     var replay=OwnedGeometryExportTargets.Prepare(f.Root,id,args);Require(Equals(replay["state"],"owned_geometry_export_target_rejected"),"Target preparation replay accepted.");
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",f.Root},{"targetId",id},{"parameters",args},{"result",result},{"leaseAccepted",leaseAccepted},{"protectedHashes",before},{"artifactHashes",Hashes(f.Root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","geometry-export-target-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});Console.WriteLine("PASS: "+rows.Count+" owned geometry export target/lease cases; no CAD or export.");return rows.Count;
  }
 }
}
