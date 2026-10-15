using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedLevelMovePlanTests {
  static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Context : NativeLevelReading.IContext {
   internal NativeLevelReadingTests.Context Inner;internal Action<int> DuringRead;internal int Reads;internal string Fault;
   internal Context(NativeLevelReadingTests.Context inner){Inner=inner;}
   public Dictionary<string,object> Observation(){Reads++;if(DuringRead!=null)DuringRead(Reads);return Inner.Observation();}
   public Dictionary<string,object> Windows(){return Inner.Windows();}
   public void ConfirmDocument(){Inner.ConfirmDocument();}
   public object LevelFilters(){return Inner.LevelFilters();}
   public object LevelSample(int number){return Inner.LevelSample(number);}
   public object LevelFields(){if(Fault=="late_levels"&&Inner.FieldsReads>=2)Inner.Fault="level_drift";return Inner.LevelFields();}
   public void Dispose(){}
  }
  internal static int Run() {
   Need(File.Exists(Path.Combine(Program.Root,".owned-level-move-fixture")),"Isolated level move fixture marker required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","double_flags","nonempty_selection","held_writer","caller_alias","held_proofs","target_bool","target_string","target_fraction","target_negative","construction998","construction999","empty_name","name_mismatch","active_bool","active_mismatch","implicit_clear","extra_parameter","no_op","multiple_targets","surface_target","set_sha","binding_sha","levels_sha","receipt_document","receipt_windows","receipt_future","receipt_missing","expired","mutation_fence","export_fence","late_fence","changed_document","late_levels","saved_change_during_guard","busy_executor","validation_tamper","validation_working_change","validation_levels_change","replay"}) {
    var fixture=new OwnedOperationPlanTests.Fixture(true,true);var inner=new NativeLevelReadingTests.Context(fixture);string root=fixture.Root,id=Guid.NewGuid().ToString("N");
    if(fault=="double_flags")inner.Fault="double_flags";
    if(fault=="nonempty_selection") {inner.Snapshot["contextBefore"]=inner.Snapshot["contextAfter"]=new[]{1,0};inner.Snapshot["selectionBefore"]=inner.Snapshot["selectionAfter"]=new[]{new Dictionary<string,object>{{"type","Solid"},{"name","Synthetic_40"}}};}
    // Refresh fixture proofs after adding the complete passive level observation.
    string sourceId=Guid.NewGuid().ToString("N");inner.Snapshot["bridgeRequestId"]=sourceId;inner.Snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");
    string sourceSha=NativeJournal.Freeze(Path.Combine(root,"responses",sourceId+".json"),inner.Snapshot);string expires=DateTime.UtcNow.AddSeconds(180).ToString("o");
    var originalBinding=DocumentBindings.Load(root,new Dictionary<string,object>{{"bindingId",fixture.BindingId},{"bindingSha256",fixture.Parameters["bindingSha256"]}});
    string bindingId=Guid.NewGuid().ToString("N");var binding=DocumentBindings.Prepare(root,bindingId,new Dictionary<string,object>{{"copyId",originalBinding["copyId"]},{"ownershipSha256",originalBinding["ownershipSha256"]},{"inspectionRequestId",sourceId},{"inspectionSha256",sourceSha},{"expiresUtc",expires}},inner.Snapshot,inner,new Dictionary<string,object>());
    var set=new Dictionary<string,object>{{"inspectionRequestId",sourceId},{"inspectionSha256",sourceSha},{"expiresUtc",fault=="expired"?DateTime.UtcNow.AddSeconds(-1).ToString("o"):expires},{"entityKeys",fault=="multiple_targets"?new[]{new[]{40,5},new[]{21,8}}:fault=="surface_target"?new[]{new[]{21,8}}:new[]{new[]{40,5}}}};
    string setId=Guid.NewGuid().ToString("N"),setSha=NativeJournal.Freeze(Path.Combine(root,"entitysets",setId+".json"),set);
    var levels=NativeLevelReading.Read(Clone(inner.Snapshot),EntitySets.Expiry(expires),inner);Need(Equals(levels["state"],"native_levels_read_unqualified"),"Fixture level receipt failed.");
    string levelId=Guid.NewGuid().ToString("N");levels["bridgeRequestId"]=levelId;
    if(fault=="receipt_document")((Dictionary<string,object>)((Dictionary<string,object>)levels["after"])["model"])["id"]=99;
    if(fault=="receipt_windows")((Dictionary<string,object>)levels["windowsAfter"])["processId"]=99;
    if(fault=="receipt_future")levels["timestampUtc"]=DateTime.UtcNow.AddHours(1).ToString("o");
    if(fault=="receipt_missing")levels.Remove("filterOrder");
    string levelPath=Path.Combine(root,"responses",levelId+".json"),levelSha=NativeJournal.Freeze(levelPath,levels);
    var parameters=new Dictionary<string,object>{{"targetLevelNumber",5},{"targetLevelName","Named unused"},{"targetActiveRaw",0},{"selectionAfterPolicy","cleared_by_documented_api"},{"mode","move_in_place"}};
    if(fault=="target_bool")parameters["targetLevelNumber"]=true;
    if(fault=="target_string")parameters["targetLevelNumber"]="5";
    if(fault=="target_fraction")parameters["targetLevelNumber"]=5.5;
    if(fault=="target_negative")parameters["targetLevelNumber"]=-1;
    if(fault=="construction998")parameters["targetLevelNumber"]=998;
    if(fault=="construction999")parameters["targetLevelNumber"]=999;
    if(fault=="empty_name")parameters["targetLevelName"]="";
    if(fault=="name_mismatch")parameters["targetLevelName"]="Other";
    if(fault=="active_bool")parameters["targetActiveRaw"]=false;
    if(fault=="active_mismatch")parameters["targetActiveRaw"]=1;
    if(fault=="implicit_clear")parameters["selectionAfterPolicy"]="preserve";
    if(fault=="extra_parameter")parameters["showTarget"]=true;
    if(fault=="no_op") {parameters["targetLevelNumber"]=0;parameters["targetLevelName"]="General";parameters["targetActiveRaw"]=1;}
    var args=new Dictionary<string,object>{{"entitySetId",setId},{"entitySetSha256",setSha},{"bindingId",bindingId},{"bindingSha256",binding["bindingSha256"]},{"levelInspectionRequestId",levelId},{"levelInspectionSha256",levelSha},{"parameters",parameters}};
    if(fault=="set_sha")args["entitySetSha256"]=new string('f',64);
    if(fault=="binding_sha")args["bindingSha256"]=new string('f',64);
    if(fault=="levels_sha")args["levelInspectionSha256"]=new string('f',64);
    var context=new Context(new NativeLevelReadingTests.Context(fixture));context.Inner.Snapshot=Clone(inner.Snapshot);context.Inner.Fault=fault=="double_flags"?"double_flags":null;
    string ownDir=Path.GetDirectoryName(fixture.Working);
    if(fault=="mutation_fence")File.WriteAllText(Path.Combine(ownDir,"mutation-fence.json"),"Owned test fence.");
    if(fault=="export_fence")File.WriteAllText(Path.Combine(ownDir,"geometry-export-fence.json"),"Owned export fence.");
    if(fault=="late_fence")context.DuringRead=n=>{if(n==3)File.WriteAllText(Path.Combine(ownDir,"mutation-fence.json"),"Late own fence.");};
    if(fault=="changed_document")((Dictionary<string,object>)context.Inner.Snapshot["model"])["id"]=99;
    if(fault=="late_levels")context.Fault="late_levels";
    if(fault=="saved_change_during_guard")context.DuringRead=n=>{if(n==1){using(var writer=new FileStream(fixture.Working,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))writer.SetLength(writer.Length+1);}};
    if(fault=="caller_alias")context.DuringRead=n=>{if(n==1){parameters["targetLevelNumber"]=999;args["bindingId"]=new string('f',32);}};
    int blocked=0;
    if(fault=="held_proofs")context.DuringRead=n=>{if(n==1){try{File.AppendAllText(levelPath,"tamper");}catch(IOException){blocked++;}}};
    var submitted=Clone(args);FileStream writerLease=null,busy=null;
    if(fault=="held_writer")writerLease=new FileStream(fixture.Working,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite);
    var before=Hashes(root);
    if(fault=="busy_executor")busy=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    Dictionary<string,object> result;try{result=OwnedLevelMovePlans.Check(root,id,args,true,context);}finally{if(writerLease!=null)writerLease.Dispose();if(busy!=null)busy.Dispose();}
    bool control=new[]{"control","double_flags","nonempty_selection","held_writer","caller_alias","held_proofs","validation_tamper","validation_working_change","validation_levels_change","replay"}.Contains(fault);
    Need(Equals(result["state"],control?"owned_level_move_prepared_observed":"owned_level_move_preparation_rejected"),fault+": "+Program.Json.Serialize(result));
    foreach(string flag in new[]{"executionEnabled","nativeProviderQualified","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted"})Need(Equals(result[flag],false),"Level preparation acquired authority.");
    Need(before.All(pair=>fault=="saved_change_during_guard"&&pair.Key==fixture.Working||Program.Hash(pair.Key)==pair.Value),"Protected fixture bytes changed.");
    if(fault=="held_proofs")Need(blocked==1,"Held receipt test did not run.");
    Dictionary<string,object> validation=null;
    if(control) {
     Need(EntitySets.Canonical(((Dictionary<string,object>)result["ownedLevelMove"])["arguments"])==EntitySets.Canonical(submitted),"Caller changed detached packet.");
     var handle=new Dictionary<string,object>{{"ownedLevelMoveId",id},{"ownedLevelMoveSha256",result["ownedLevelMoveSha256"]}};
     if(fault=="validation_tamper")File.AppendAllText(Path.Combine(root,"ownedlevelmoves",id+".json"),"tamper");
     if(fault=="validation_working_change")File.AppendAllText(fixture.Working,"Own test change after preparation.");
     if(fault=="validation_levels_change")context.Inner.Fault="level_drift";
     var validationHashes=Hashes(root);
     validation=OwnedLevelMovePlans.Check(root,fault=="replay"?id:Guid.NewGuid().ToString("N"),fault=="replay"?submitted:handle,fault=="replay",context);
     bool accepted=!fault.StartsWith("validation_",StringComparison.Ordinal)&&fault!="replay";
     Need(Equals(validation["state"],accepted?"owned_level_move_validated_observed":"owned_level_move_preparation_rejected"),fault+" validation: "+Program.Json.Serialize(validation));
     Need(validationHashes.All(pair=>Program.Hash(pair.Key)==pair.Value),"Validation changed prior files.");
    }
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"requestId",id},{"submitted",submitted},{"result",result},{"validation",validation},{"protectedHashes",before},{"artifactHashes",Hashes(root)},{"observationReads",context.Reads},{"blockedWrites",blocked},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-level-move-plan-tests.json"),new Dictionary<string,object>{{"state","passed"},{"schema","psbridge.owned-level-move-plan-matrix.v1"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" owned level move preparation/validation cases; fake contexts/no CAD.");return rows.Count;
  }
 }
}
