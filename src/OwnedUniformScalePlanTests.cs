using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedUniformScalePlanTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-uniform-scale-fixture")),"Isolated uniform scale fixture marker required; no CAD.");
   int configRefusals=0;
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));
   config["ownedUniformScalePlansExperimental"]=true;foreach(string flag in new[]{"entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})config[flag]=true;
   Program.ParseConfig(Program.Json.Serialize(config));
   foreach(string flag in new[]{"ownedUniformScalePlansExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"}) {
    var changed=new Dictionary<string,object>(config);changed[flag]="true";bool denied=false;try{Program.ParseConfig(Program.Json.Serialize(changed));}catch(ArgumentException){denied=true;}
    Require(denied,"Coerced scale configuration accepted.");configRefusals++;
   }
   var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","existing_writer","mixed_targets","shrink","caller_alias","held_proofs","saved_bytes_changed_before_prepare","working_write_during_guard","factor_bool","factor_string","factor_zero","factor_negative","factor_one","missing_centre","implicit_frame","keep_original","axis_lock","set_sha","binding_sha","set_expired","absent_target","source_drift","mutation_fence","mutation_fence_directory","export_fence","late_fence","changed_document","changed_units","changed_workplane","nonempty_selection","late_context","checkpoint_writable","busy_executor"}) {
    var f=new OwnedOperationPlanTests.Fixture(true,true);var context=f.NewContext();string root=f.Root,id=Guid.NewGuid().ToString("N");
    var set=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"entitysets",f.SetId+".json")));
    if(fault=="mixed_targets")set["entityKeys"]=new[]{new[]{40,5},new[]{21,8},new[]{59,10}};
    if(fault=="set_expired")set["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    if(fault=="absent_target")set["entityKeys"]=new[]{new[]{40,999}};
    if(fault=="source_drift") {
     var source=Clone(f.Snapshot);source["lengthUnitFactorRaw"]=2;string sourceId=Guid.NewGuid().ToString("N");
     set["inspectionRequestId"]=sourceId;set["inspectionSha256"]=NativeJournal.Freeze(Path.Combine(root,"responses",sourceId+".json"),source);
    }
    string setId=Guid.NewGuid().ToString("N"),setPath=Path.Combine(root,"entitysets",setId+".json"),setSha=NativeJournal.Freeze(setPath,set);
    var parameters=new Dictionary<string,object>{{"factor",fault=="shrink"?0.5:2},{"centre",new[]{10,20,30}},{"coordinateFrame","world"},{"lengthUnits","document_raw"},{"mode","scale_in_place"}};
    if(fault=="factor_bool")parameters["factor"]=true;
    if(fault=="factor_string")parameters["factor"]="2";
    if(fault=="factor_zero")parameters["factor"]=0;
    if(fault=="factor_negative")parameters["factor"]=-2;
    if(fault=="factor_one")parameters["factor"]=1;
    if(fault=="missing_centre")parameters.Remove("centre");
    if(fault=="implicit_frame")parameters["coordinateFrame"]="active_workplane";
    if(fault=="keep_original")parameters["mode"]="copy";
    if(fault=="axis_lock")parameters["lockX"]=true;
    var args=new Dictionary<string,object>{{"entitySetId",setId},{"entitySetSha256",setSha},{"bindingId",f.Parameters["bindingId"]},{"bindingSha256",f.Parameters["bindingSha256"]},{"parameters",parameters}};
    if(fault=="set_sha")args["entitySetSha256"]=new string('f',64);
    if(fault=="binding_sha")args["bindingSha256"]=new string('f',64);
    string ownDir=Path.GetDirectoryName(f.Working);
    if(fault=="mutation_fence")File.WriteAllText(Path.Combine(ownDir,"mutation-fence.json"),"Own unresolved fixture.");
    if(fault=="mutation_fence_directory")Directory.CreateDirectory(Path.Combine(ownDir,"mutation-fence.json"));
    if(fault=="export_fence")File.WriteAllText(Path.Combine(ownDir,"geometry-export-fence.json"),"Own unresolved export fixture.");
    if(fault=="late_fence")context.DuringRead=n=>{if(n==4)File.WriteAllText(Path.Combine(ownDir,"mutation-fence.json"),"Late unresolved fixture.");};
    if(fault=="changed_document")((Dictionary<string,object>)context.Snapshot["model"])["id"]=99;
    if(fault=="changed_units")context.Snapshot["lengthUnitFactorRaw"]=2;
    if(fault=="changed_workplane")context.Snapshot["contextAfter"]=new[]{1,0};
    if(fault=="nonempty_selection")context.Snapshot["selectionAfter"]=new[]{5};
    if(fault=="late_context")context.DuringRead=n=>{if(n==4)context.Changed=true;};
    if(fault=="caller_alias")context.DuringRead=n=>{if(n==1){parameters["factor"]=100;((int[])parameters["centre"])[0]=999;args["bindingId"]=new string('f',32);}};
    int blockedWrites=0;
    if(fault=="held_proofs")context.DuringRead=n=>{if(n==3) {
     foreach(string path in new[]{setPath,Path.Combine(root,"ownedscales",id+".json"),Path.Combine(root,"operations",id,"000000.json")}) {
      bool denied=false;try{File.AppendAllText(path,"tamper");}catch(IOException){denied=true;}Require(denied,"Held scale proof was writable.");blockedWrites++;
     }
    }};
    var owner=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(ownDir,"ownership.json")));string cp=(string)((Dictionary<string,object>)owner["checkpoint"])["path"];
    if(fault=="checkpoint_writable")File.SetAttributes(cp,FileAttributes.Normal);
    // Owned saved files are legitimately mutable before a new lease is taken.
    // Preparation pins their current bytes, not the ownership initial bytes.
    if(fault=="saved_bytes_changed_before_prepare")File.AppendAllText(f.Working,"Own synthetic saved-byte change before current lease.");
    if(fault=="working_write_during_guard")context.DuringRead=n=>{if(n==1)File.AppendAllText(f.Working,"Denied change during current held lease.");};
    var submitted=Clone(args);var before=Hashes(root);FileStream busy=null;
    if(fault=="busy_executor")busy=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    FileStream existingWriter=fault=="existing_writer"?new FileStream(f.Working,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite):null;
    Dictionary<string,object> result;try{result=OwnedUniformScalePlans.Prepare(root,id,args,context);}finally{if(existingWriter!=null)existingWriter.Dispose();if(busy!=null)busy.Dispose();}
    bool control=new[]{"control","existing_writer","mixed_targets","shrink","caller_alias","held_proofs","saved_bytes_changed_before_prepare"}.Contains(fault);
    Require(Equals(result["state"],control?"owned_uniform_scale_prepared_observed":"owned_uniform_scale_preparation_rejected"),fault+": "+Program.Json.Serialize(result));
    Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),fault+": protected fixture bytes changed.");
    foreach(string flag in new[]{"executionEnabled","geometryMutationAttempted","contextChangeAttempted","cadSaveAttempted","nativeProviderQualified"})Require(Equals(result[flag],false),"Scale preparation gave action authority.");
    if(control)Require(context.Reads==4&&Equals(result["guardPasses"],2),"Missing two guards/four reads.");
    if(fault=="held_proofs")Require(blockedWrites==3,"Proof-lock check did not execute.");
    if(fault=="caller_alias")Require(EntitySets.Canonical(((Dictionary<string,object>)result["ownedScale"])["parameters"])==EntitySets.Canonical(submitted["parameters"]),"Caller alias changed immutable plan.");
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"requestId",id},{"submitted",submitted},{"result",result},{"protectedHashes",before},{"artifactHashes",Hashes(root)},{"observationReads",context.Reads},{"blockedWrites",blockedWrites},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-uniform-scale-plan-tests.json"),new Dictionary<string,object>{{"state","passed"},{"schema","psbridge.owned-uniform-scale-plan-matrix.v1"},{"rows",rows},{"configurationRefusals",configRefusals},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" owned uniform scale preparation cases; fake contexts/no CAD.");return rows.Count;
  }
 }
}
