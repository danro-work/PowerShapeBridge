using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedDgkExportTests {
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Context : OwnedDgkExport.IContext {
   internal readonly OwnedOperationPlanTests.Fixture Fixture;
   internal Dictionary<string,object> Snapshot,Listing,Settings;
   internal readonly List<Dictionary<string,object>> Selected=new List<Dictionary<string,object>>();
   internal readonly List<int[]> Keys=new List<int[]>();
   internal string Fault,Primary;internal int Adds,Exports,Clears,Reads,MapReads;internal bool Changed;
   internal Context(OwnedOperationPlanTests.Fixture fixture,Dictionary<string,object> parameters) {
    Fixture=fixture;Snapshot=Clone(fixture.Snapshot);Listing=Clone(fixture.Listing);
    Settings=new Dictionary<string,object>{{"format","dgk"},{"coordinateFrame",parameters["coordinateFrame"]},{"exportVariant",parameters["exportVariant"]},
     {"triangleCompanions",parameters["triangleCompanions"]},{"featureCompanions",parameters["featureCompanions"]},{"effectiveOptionsSha256",new string('b',64)},{"providerQualification","synthetic_only"}};
   }
   public Dictionary<string,object> Windows(){var value=Clone(Listing);if(Fault=="changed_windows"&&Adds>0)value["processId"]=99;return value;}
   public Dictionary<string,object> Observation() {
    Reads++;var value=Clone(Snapshot);value["contextBefore"]=value["contextAfter"]=new[]{Selected.Count,Fault=="world_after_select"&&Adds>0?1:0};
    value["selectionBefore"]=value["selectionAfter"]=Fault=="reordered_selection"?Selected.AsEnumerable().Reverse().ToArray():Selected.ToArray();
    if(Fault=="late_document"&&Adds>0||Fault=="claimed_context"&&Reads>=3)((Dictionary<string,object>)value["model"])["id"]=99;
    if(Fault=="process_after_export"&&Exports>0)value["processId"]=99;return value;
   }
   public void ConfirmDocument(){if(Changed)throw new InvalidOperationException("Retained fake document changed.");}
   public Dictionary<string,object> ExportSettings(){return Settings;}
   public int Composite(int type,int id){MapReads++;return Fault=="wrong_mapping"&&MapReads==1?999:type*100000+id;}
   public int[][] SelectedKeys(){if(Fault=="selected_keys_null")return null;return Fault=="reordered_selection"?Keys.AsEnumerable().Reverse().ToArray():Keys.ToArray();}
   public bool Busy(){return Fault=="busy_before"||Fault=="busy_after_select"&&Adds>0;}
   public void AddSelection(int type,int id,string name) {
    Adds++;Selected.Add(new Dictionary<string,object>{{"type",type==40?"SOLID":type==21?"SURFACE":"COMPCURVE"},{"name",name}});Keys.Add(new[]{type,id});
    if(Fault=="wrong_selection")Selected.Last()["name"]="Foreign";
    if(Fault=="wrong_selected_keys")Keys[Keys.Count-1]=new[]{type,999};
    if(Fault=="select_throw")throw new IOException("Unknown selection callback.");
    if(Fault=="settings_after_select")Settings["effectiveOptionsSha256"]=new string('f',64);
    if(Fault=="selection_collision")File.WriteAllText(Primary,"Own collision before export.");
    if(Fault=="retained_document")Changed=true;
   }
   public void ExportDgk(string path,Dictionary<string,object> parameters) {
    Exports++;
    if(Fault=="export_throw")throw new IOException("Unknown export callback.");
    if(Fault=="no_output")return;
    if(Fault=="output_directory"){Directory.CreateDirectory(path);return;}
    File.WriteAllText(path,"Opaque own synthetic DGK bytes, not a CAD model.");
    if(Fault=="lost_reply")throw new IOException("Export effect followed by lost return.");
    if(Fault=="missing_companion")return;
    foreach(var item in ((System.Collections.IList)parameters["triangleCompanions"]).Cast<Dictionary<string,object>>()) {
     string target=path.Substring(0,path.Length-4)+(string)item["suffix"];
     if(Fault=="malformed_dmt")File.WriteAllText(target,"Bad DMT.");
     else new OwnedGeometryExportTests.Context(Fixture).ExportDmt(target);
    }
    foreach(var item in ((System.Collections.IList)parameters["featureCompanions"]).Cast<Dictionary<string,object>>())File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),(string)item["name"]),"Opaque feature companion; never execute.");
    if(Fault=="stray_output")File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),"stray.xml"),"Undeclared.");
    if(Fault=="settings_after_export")Settings["effectiveOptionsSha256"]=new string('f',64);
   }
   public void ClearOwnSelection() {
    Clears++;
    if(Fault=="output_write_during_clear")File.WriteAllText(Primary,"Attempted replacement of retained output.");
    if(Fault=="clear_no_effect")return;Selected.Clear();Keys.Clear();
    if(Fault=="clear_throw")throw new IOException("Own clear effect followed by lost return.");
    if(Fault=="extra_during_clear")File.WriteAllText(Path.Combine(Path.GetDirectoryName(Primary),"late.xml"),"Late undeclared file.");
   }
   public void Dispose(){}
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-dgk-export-fixture")),"Isolated DGK export marker required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   string[] faults={"control","curve","mixed","reordered_selection","all_companions","inactive_level","ambiguous_name","unsafe_name","wrong_mapping","selected_keys_null",
    "unsupported_provider","wrong_settings_frame","extra_settings","target_sha","busy_executor","busy_before","busy_after_select","claimed_context","select_throw","wrong_selection","wrong_selected_keys",
    "late_document","retained_document","changed_windows","world_after_select","settings_after_select","selection_collision","export_throw","lost_reply","no_output",
    "output_directory","missing_companion","malformed_dmt","stray_output","process_after_export","settings_after_export","clear_throw","clear_no_effect","output_write_during_clear","extra_during_clear","existing_export_fence"};
   foreach(string fault in faults) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);fixture.Snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","Synthetic"},{"activeRaw",fault=="inactive_level"?0:1}}};
    var inventory=EntitySets.Inventory(fixture.Snapshot);if(fault=="ambiguous_name"){inventory[1]["type"]="SOLID";inventory[1]["name"]=inventory[0]["name"];}if(fault=="unsafe_name")inventory[0]["name"]="Cannot ' inject";
    var keys=fault=="curve"?new[]{new[]{59,10}}:fault=="mixed"||fault=="reordered_selection"?new[]{new[]{40,5},new[]{21,8},new[]{59,10}}:new[]{new[]{40,5}};
    var request=OwnedGeometryExportTargetTests.Rebind(fixture,fixture.Snapshot,null,keys);
    var parameters=new Dictionary<string,object>{{"coordinateFrame","world"},{"exportVariant","geometry"},{"triangleCompanions",new object[0]},{"featureCompanions",new object[0]},{"settingsQualification","planned_unqualified"}};
    if(fault=="all_companions"||fault=="missing_companion") {
     parameters["exportVariant"]="geometry_features";parameters["featureCompanions"]=new[]{new Dictionary<string,object>{{"name","explicit_holes.mac"},{"role","features_mac"},{"required",true}},new Dictionary<string,object>{{"name","explicit_holes.xml"},{"role","features_xml"},{"required",false}}};
    }
    if(fault=="all_companions"||fault=="malformed_dmt")parameters["triangleCompanions"]=new[]{new Dictionary<string,object>{{"suffix",".dmt"},{"required",false}},new Dictionary<string,object>{{"suffix",".dgk.dmt"},{"required",true}}};
    request["parameters"]=parameters;string targetId=Guid.NewGuid().ToString("N");var target=OwnedDgkTargets.Prepare(fixture.Root,targetId,request);
    Require(Equals(target["state"],"owned_dgk_target_prepared_files"),"DGK export target fixture failed: "+Program.Json.Serialize(target));
    var context=new Context(fixture,Clone(parameters)){Fault=fault,Primary=(string)((Dictionary<string,object>)target["target"])["targetPath"]};
    if(fault=="unsupported_provider")context.Settings["providerQualification"]="qualified";
    if(fault=="wrong_settings_frame")context.Settings["coordinateFrame"]="active_workplane";
    if(fault=="extra_settings")context.Settings["command"]="Never execute";
    var args=new Dictionary<string,object>{{"targetId",targetId},{"targetSha256",fault=="target_sha"?new string('f',64):target["targetSha256"]}};
    var binding=Read(Path.Combine(fixture.Root,"documentbindings",(string)request["bindingId"]+".json"));string fence=OwnedGeometryExportTargets.ExportFencePath(fixture.Root,(string)binding["copyId"]);
    if(fault=="existing_export_fence")File.WriteAllText(fence,"Prior unknown own export.");
    var before=Hashes(fixture.Root);FileStream busy=fault=="busy_executor"?new FileStream(Path.Combine(fixture.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None):null;
    string id=Guid.NewGuid().ToString("N");Dictionary<string,object> result;
    try{result=OwnedDgkExport.Execute(fixture.Root,id,args,context);}finally{if(busy!=null)busy.Dispose();}
    bool control=new[]{"control","curve","mixed","reordered_selection","all_companions"}.Contains(fault),changed=context.Adds+context.Exports+context.Clears>0;
    Require(Equals(result["state"],control?"owned_dgk_export_executed_unverified":changed?"owned_dgk_export_outcome_unknown":"owned_dgk_export_rejected_before_change"),fault+": "+Program.Json.Serialize(result));
    Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"DGK export changed protected fixture bytes.");
    foreach(string flag in new[]{"geometryMutationAttempted","cadSaveAttempted","geometryVerified","nativeProviderQualified","nativeReturnCodeQualified","retryAllowed","copyFenceReleased"})Require(Equals(result[flag],false),"Unexpected geometry/provider authority.");
    if(control)Require(context.Adds==keys.Length&&context.Exports==1&&context.Clears==1&&context.Keys.Count==0&&Equals(result["recordedJournalState"],"executed_unverified"),"DGK callback sequence differs.");
    if(changed&&!control&&context.Clears==0)Require(context.Keys.Count>0,"Unknown selection was silently cleared.");
    int prior=context.Adds+context.Exports+context.Clears;
    if(changed||File.Exists(fence)) {var replay=OwnedDgkExport.Execute(fixture.Root,Guid.NewGuid().ToString("N"),args,context);Require(Equals(replay["state"],"owned_dgk_export_rejected_before_change")&&context.Adds+context.Exports+context.Clears==prior,"DGK executor replay issued a callback.");}
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",fixture.Root},{"targetId",targetId},{"operationId",id},{"result",result},
     {"adds",context.Adds},{"exports",context.Exports},{"clears",context.Clears},{"selectedKeys",context.Keys.ToArray()},
     {"protectedHashes",before},{"artifactHashes",Hashes(fixture.Root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-dgk-export-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" guarded DGK export callback cases; no CAD.");return rows.Count;
  }
  static Dictionary<string,object> Read(string path){return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
 }
}
