using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedDgkTargetTests {
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,object> Read(string path){return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Program.Hash(p));}
  static void Reject(Action action){bool denied=false;try{action();}catch(Exception error){if(!(error is IOException)&&!(error is InvalidDataException)&&!(error is InvalidOperationException)&&!(error is ArgumentException))throw;denied=true;}Require(denied,"Expected DGK guard refusal.");}
  static Dictionary<string,object> Parameters() {
   return new Dictionary<string,object>{{"coordinateFrame","world"},{"exportVariant","geometry"},{"triangleCompanions",new object[0]},
    {"featureCompanions",new object[0]},{"settingsQualification","planned_unqualified"}};
  }
  static Dictionary<string,object> Args(OwnedOperationPlanTests.Fixture f) {
   return new Dictionary<string,object>{{"bindingId",f.BindingId},{"bindingSha256",f.Parameters["bindingSha256"]},
    {"entitySetId",f.SetId},{"entitySetSha256",Program.Hash(Path.Combine(f.Root,"entitysets",f.SetId+".json"))},{"parameters",Parameters()}};
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-dgk-fixture")),"Explicit isolated DGK fixture marker required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   string[] faults={"primary","curve","mixed","all_companions","working_byte_capture","caller_mutation",
    "wrong_binding_sha","wrong_set_sha","extra_path","world_required","settings_claim","unknown_variant","bad_triangle_suffix","duplicate_triangle",
    "features_missing","features_on_geometry","optional_only_features","feature_path","feature_wrong_role","numeric_required","expired","selection",
    "unknown_fence","export_fence","checkpoint_writable","reserved","existing_folder","held_writer",
    "output_file","output_directory","stray_file","claimed","late_fence","late_working","target_path_tamper","manifest_tamper","expiry_tamper","future_prepared_tamper","parameters_tamper","directory_replaced","bundle_replaced","target_tamper"};
   var prepared=new HashSet<string>(new[]{"primary","curve","mixed","all_companions","working_byte_capture","caller_mutation", "output_file","output_directory","stray_file","claimed","late_fence","late_working","target_path_tamper","manifest_tamper","expiry_tamper","future_prepared_tamper","parameters_tamper","directory_replaced","bundle_replaced","target_tamper"});
   var leaseOk=new HashSet<string>(new[]{"primary","curve","mixed","all_companions","working_byte_capture","caller_mutation"});
   foreach(string fault in faults) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);var args=Args(fixture);string id=Guid.NewGuid().ToString("N");
    if(fault=="curve"||fault=="mixed") {
     args=OwnedGeometryExportTargetTests.Rebind(fixture,fixture.NewContext().Snapshot,null,fault=="curve"?new[]{new[]{59,10}}:new[]{new[]{40,5},new[]{21,8},new[]{59,10}});args["parameters"]=Parameters();
    }
    var parameters=(Dictionary<string,object>)args["parameters"];
    if(fault=="all_companions") {
     parameters["exportVariant"]="geometry_features";
     parameters["triangleCompanions"]=new[]{new Dictionary<string,object>{{"suffix",".dmt"},{"required",false}},new Dictionary<string,object>{{"suffix",".dgk.dmt"},{"required",true}}};
     parameters["featureCompanions"]=new[]{new Dictionary<string,object>{{"name","explicit_holes.mac"},{"role","features_mac"},{"required",true}},new Dictionary<string,object>{{"name","explicit_holes.xml"},{"role","features_xml"},{"required",false}}};
    }
    if(fault=="working_byte_capture")File.AppendAllText(fixture.Working,"Own legitimate saved working bytes changed before preparation.");
    if(fault=="wrong_binding_sha")args["bindingSha256"]=new string('f',64);
    if(fault=="wrong_set_sha")args["entitySetSha256"]=new string('f',64);
    if(fault=="extra_path")args["targetPath"]=fixture.Working;
    if(fault=="world_required")parameters["coordinateFrame"]="active_workplane";
    if(fault=="settings_claim")parameters["settingsQualification"]="qualified";
    if(fault=="unknown_variant")parameters["exportVariant"]="default";
    if(fault=="bad_triangle_suffix"||fault=="duplicate_triangle")parameters["triangleCompanions"]=fault=="bad_triangle_suffix"?
     new[]{new Dictionary<string,object>{{"suffix","../../file.dmt"},{"required",true}}}:
     new[]{new Dictionary<string,object>{{"suffix",".dmt"},{"required",true}},new Dictionary<string,object>{{"suffix",".dmt"},{"required",false}}};
    if(new[]{"features_missing","optional_only_features","feature_path","feature_wrong_role","numeric_required"}.Contains(fault))parameters["exportVariant"]="geometry_features";
    if(new[]{"features_on_geometry","optional_only_features","feature_path","feature_wrong_role","numeric_required"}.Contains(fault))
     parameters["featureCompanions"]=new[]{new Dictionary<string,object>{{"name",fault=="feature_path"?"../holes.mac":"holes.mac"},{"role",fault=="feature_wrong_role"?"triangles_dmt":"features_mac"},{"required",fault=="numeric_required"?(object)1:fault!="optional_only_features"}}};
    if(fault=="expired") {var set=Read(Path.Combine(fixture.Root,"entitysets",fixture.SetId+".json"));set["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");File.WriteAllText(Path.Combine(fixture.Root,"entitysets",fixture.SetId+".json"),Program.Json.Serialize(set));args["entitySetSha256"]=Program.Hash(Path.Combine(fixture.Root,"entitysets",fixture.SetId+".json"));}
    if(fault=="selection") {var snapshot=fixture.NewContext().Snapshot;snapshot["selectionBefore"]=snapshot["selectionAfter"]=new[]{40};args=OwnedGeometryExportTargetTests.Rebind(fixture,snapshot);args["parameters"]=parameters;}
    var binding=Read(Path.Combine(fixture.Root,"documentbindings",(string)args["bindingId"]+".json"));string copyId=(string)binding["copyId"];
    string fence=OwnedTranslation.FencePath(fixture.Root,copyId),exportFence=OwnedGeometryExportTargets.ExportFencePath(fixture.Root,copyId);
    if(fault=="unknown_fence")File.WriteAllText(fence,"Unknown own synthetic operation.");
    if(fault=="export_fence")File.WriteAllText(exportFence,"Unknown own synthetic export.");
    if(fault=="checkpoint_writable") {var owner=Read(Path.Combine(Path.GetDirectoryName(fixture.Working),"ownership.json"));File.SetAttributes((string)((Dictionary<string,object>)owner["checkpoint"])["path"],FileAttributes.Normal);}
    string folder=OwnedDgkTargets.Folder(fixture.Root,id),bundle=OwnedDgkTargets.BundleFolder(fixture.Root,id);
    if(fault=="reserved") {Directory.CreateDirectory(Path.GetDirectoryName(folder));File.WriteAllText(folder+".reservation.json","Existing own reservation.");}
    if(fault=="existing_folder")Directory.CreateDirectory(folder);
    FileStream writer=fault=="held_writer"?new FileStream(fixture.Working,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite):null;
    var before=Hashes(fixture.Root);var request=Clone(args);Dictionary<string,object> result;
    try{result=OwnedDgkTargets.Prepare(fixture.Root,id,args);}finally{if(writer!=null)writer.Dispose();}
    Require(Equals(result["state"],prepared.Contains(fault)?"owned_dgk_target_prepared_files":"owned_dgk_target_rejected"),fault+": "+Program.Json.Serialize(result));
    Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"DGK preparation changed protected input.");
    foreach(string flag in new[]{"executionEnabled","exportAttempted","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","powerShapeConnectionAttempted","retryAllowed","exportSettingsQualified","nativeProviderQualified"})Require(Equals(result[flag],false),"Unexpected DGK execution/qualification flag.");
    bool? accepted=null;
    if(prepared.Contains(fault)) {
     string sha=(string)result["targetSha256"],targetJson=Path.Combine(folder,"target.json");var plan=Read(targetJson);
     using(var lease=new OwnedDgkTargets.TargetLease(fixture.Root,id,sha)) {
      lease.Recheck();Reject(()=>File.AppendAllText(fixture.Working,"forbidden held write"));Reject(()=>Directory.Move(bundle,bundle+"-moved"));Reject(()=>File.AppendAllText(targetJson,"forbidden proof write"));
     }
     if(fault=="caller_mutation") {parameters["exportVariant"]="invalid caller mutation";Require(Equals(((Dictionary<string,object>)Read(targetJson)["parameters"])["exportVariant"],"geometry"),"DGK plan retained caller aliases.");}
     if(fault=="output_file")File.WriteAllText((string)plan["targetPath"],"Own collision.");
     if(fault=="output_directory")Directory.CreateDirectory((string)plan["targetPath"]);
     if(fault=="stray_file")File.WriteAllText(Path.Combine(bundle,"stray.xml"),"Own stray artifact.");
     if(fault=="claimed")File.WriteAllText(Path.Combine(folder,"export-once.json"),"Own prior claim.");
     if(fault=="late_fence")File.WriteAllText(fence,"New unknown own operation.");
     if(fault=="late_working")File.AppendAllText(fixture.Working,"Changed after byte capture.");
     if(new[]{"target_path_tamper","manifest_tamper","expiry_tamper","future_prepared_tamper","parameters_tamper"}.Contains(fault)) {
      if(fault=="target_path_tamper")plan["targetPath"]=fixture.Working;
      if(fault=="manifest_tamper")plan["artifactManifest"]=new object[0];
      if(fault=="expiry_tamper")plan["expiresUtc"]=DateTime.UtcNow.AddSeconds(400).ToString("o");
      if(fault=="future_prepared_tamper")plan["preparedUtc"]=DateTime.UtcNow.AddSeconds(400).ToString("o");
      if(fault=="parameters_tamper")((Dictionary<string,object>)plan["parameters"])["triangleCompanions"]=new[]{new Dictionary<string,object>{{"suffix",".dmt"},{"required",false}}};
      File.WriteAllText(targetJson,Program.Json.Serialize(plan));sha=Program.Hash(targetJson);
     }
     if(fault=="directory_replaced") {Directory.Move(folder,folder+"-original");Directory.CreateDirectory(folder);Directory.CreateDirectory(bundle);File.Copy(Path.Combine(folder+"-original","target.json"),targetJson);}
     if(fault=="bundle_replaced") {Directory.Move(bundle,bundle+"-original");Directory.CreateDirectory(bundle);}
     if(fault=="target_tamper")File.AppendAllText(targetJson," ");
     accepted=leaseOk.Contains(fault);
     if(accepted.Value)using(var lease=new OwnedDgkTargets.TargetLease(fixture.Root,id,sha)){lease.Recheck();}
     else Reject(()=>{using(var lease=new OwnedDgkTargets.TargetLease(fixture.Root,id,sha)){lease.Recheck();}});
     Require(Equals(OwnedDgkTargets.Prepare(fixture.Root,id,request)["state"],"owned_dgk_target_rejected"),"DGK target preparation replay accepted.");
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",fixture.Root},{"targetId",id},{"parameters",request},{"result",result},{"leaseAccepted",accepted},{"protectedHashes",before},{"artifactHashes",Hashes(fixture.Root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-dgk-target-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" owned DGK target/lease cases; no CAD/export.");return rows.Count;
  }
 }
}
