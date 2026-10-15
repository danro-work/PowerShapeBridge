using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShape.Shared;

namespace PowerShapeBridge {
 internal static class OwnedWorkplanePlans {
  internal interface IReadContext : DocumentBindings.IContext {
   int Composite(int type,int nativeId); double[] Bounds(int composite);
   object WorkplaneIds(); object WorkplaneInfo(int composite); int ActiveWorkplane();
  }
  internal interface IContext : IReadContext { object CreateWorkplane(double[] frame); void ActivateWorld(); }
  internal static void Need(bool ok,string reason){if(!ok)throw new InvalidDataException(reason);}
  internal static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal static void Fields(Dictionary<string,object> value,params string[] names){Need(value!=null&&value.Count==names.Length&&names.All(value.ContainsKey),"Exact workplane fields required.");}
  internal static double[] Frame(Dictionary<string,object> value) {
   Fields(value,"originWorldRaw","zAxis","yAxis","coordinateFrame","lengthUnits","activeAfter","mode");
   Need(Equals(value["coordinateFrame"],"world")&&Equals(value["lengthUnits"],"document_raw")&&Equals(value["activeAfter"],"world")&&Equals(value["mode"],"create_new"),"Explicit world/document-raw/new-workplane/world-after policy required.");
   var result=new List<double>();foreach(string key in new[]{"originWorldRaw","zAxis","yAxis"}) {
    var vector=value[key] as IList;Need(vector!=null&&vector.Count==3,"Three numeric coordinates required.");result.AddRange(vector.Cast<object>().Select(Program.StrictDouble));
   }
   var frame=result.ToArray();WorkplaneFrame.Validate(frame);return frame;
  }
  internal static void ValidateArguments(Dictionary<string,object> args,bool prepare) {
   if(prepare){Fields(args,"bindingId","bindingSha256","parameters");Frame(args["parameters"] as Dictionary<string,object>);}
   else Fields(args,"ownedWorkplaneId","ownedWorkplaneSha256");
   foreach(var key in args.Keys.Where(key=>key!="parameters"))Need(EntitySets.Hex(args[key],key.EndsWith("Sha256",StringComparison.Ordinal)?64:32),"Invalid workplane proof token.");
  }
  internal static string Artifact(string root,string folder,object id) {
   Need(EntitySets.Hex(id,32),"Invalid workplane artifact identity.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;
  }
  internal static void WorldEmpty(Dictionary<string,object> observation,IReadContext context) {
   Need(EntitySets.Canonical(observation["contextBefore"])=="[0,0]"&&EntitySets.Canonical(observation["contextAfter"])=="[0,0]"&&EntitySets.Canonical(observation["selectionBefore"])=="[]"&&EntitySets.Canonical(observation["selectionAfter"])=="[]"&&context.ActiveWorkplane()==0,"World frame and empty selection required for the first owned creation provider.");
  }
  internal static double[] Info(object raw) {
   var array=raw as Array;Need(array!=null&&array.Rank==1&&array.Length==9,"One-dimensional nine-value native workplane array required.");
   var info=array.Cast<object>().Select(Program.StrictDouble).ToArray();WorkplaneFrame.Validate(info);return info;
  }
  internal static Dictionary<string,object> Frames(Dictionary<string,object> observation,IReadContext context) {
   context.ConfirmDocument();var entries=EntitySets.Inventory(observation).Where(e=>Equals(e["nativeTypeCode"],54)).OrderBy(e=>(int)e["nativeId"]).ToArray();
   var ids=entries.Select(e=>context.Composite(54,(int)e["nativeId"])).ToArray();object raw=context.WorkplaneIds();var array=raw as Array;
   if(raw==null)Need(ids.Length==0,"Null workplane getter with nonempty native inventory.");
   else Need(array!=null&&array.Rank==1&&array.Length==ids.Length&&array.Cast<object>().Select(Program.StrictInt).OrderBy(n=>n).SequenceEqual(ids.OrderBy(n=>n)),"Workplane getter/inventory composite mapping differs.");
   var frames=entries.Select((e,i)=>new Dictionary<string,object>{{"nativeId",e["nativeId"]},{"compositeId",ids[i]},{"worldInfo",Info(context.WorkplaneInfo(ids[i]))}}).ToArray();
   return new Dictionary<string,object>{{"frames",frames},{"nullWithEmptyInventoryUnqualified",raw==null},{"getterRuntimeType",raw==null?null:raw.GetType().FullName},{"getterLowerBound",array==null?(object)null:array.GetLowerBound(0)}};
  }
  internal static Dictionary<string,object>[] Bounds(Dictionary<string,object> observation,IReadContext context) {
   return EntitySets.Inventory(observation).Where(e=>!Equals(e["nativeTypeCode"],54)).Select(e=>{
    int type=(int)e["nativeTypeCode"],native=(int)e["nativeId"],composite=context.Composite(type,native);var bounds=context.Bounds(composite);
    LevelMovePostconditions.Bounds(bounds,bounds);return new Dictionary<string,object>{{"nativeTypeCode",type},{"nativeId",native},{"compositeId",composite},{"boundsWorldRaw",bounds}};
   }).ToArray();
  }
  internal static Dictionary<string,object> Check(string root,string id,Dictionary<string,object> input,bool prepare,IReadContext context,
   Action<Dictionary<string,object>,Dictionary<string,object>,OwnedCopies.OwnershipLease,OwnedOperationPlans.Proofs> execute=null) {
   var result=new Dictionary<string,object>{{"state","owned_workplane_preparation_rejected"},{"executionEnabled",false},{"nativeProviderQualified",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false}};
   try {
    Need(EntitySets.Hex(id,32),"Invalid request identity.");ValidateArguments(input,prepare);var args=Clone(input);
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     string packetId=prepare?id:(string)args["ownedWorkplaneId"],packetSha=prepare?null:(string)args["ownedWorkplaneSha256"];Dictionary<string,object> packet=null;
     if(!prepare) {
      packet=proofs.Json(Artifact(root,"ownedworkplanes",packetId),packetSha,65536);
      Fields(packet,"schemaVersion","ownedWorkplaneId","operation","preparedUtc","expiresUtc","executionEnabled","nativeProviderQualified","arguments","copyId","ownershipSha256","workingSha256","nativeObservedPropertiesSha256","windowContextSha256","requiredBeforeExecution");
      Need(Equals(packet["schemaVersion"],1)&&Equals(packet["ownedWorkplaneId"],packetId)&&Equals(packet["operation"],"create_world_workplane")&&Equals(packet["executionEnabled"],false)&&Equals(packet["nativeProviderQualified"],false),"Prepared workplane policy differs.");
      OwnedOperationPlans.Prepared(root,packetId,packetSha,proofs);args=Clone((Dictionary<string,object>)packet["arguments"]);ValidateArguments(args,true);
     }else Need(!File.Exists(Artifact(root,"ownedworkplanes",packetId))&&!Directory.Exists(Artifact(root,"ownedworkplanes",packetId)),"Prepared workplane identity already exists.");
     var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};
     var loaded=DocumentBindings.Load(root,bindingArgs);var binding=proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);
     Need(EntitySets.Canonical(loaded)==EntitySets.Canonical(binding),"Binding changed during load.");
     var expected=proofs.Json(Artifact(root,"responses",binding["inspectionRequestId"]),(string)binding["inspectionSha256"],16000000);
     DateTime expiry=EntitySets.Expiry(binding["expiresUtc"]);EntitySets.CheckObserved(expected,expected,new int[0][],expiry,DateTime.UtcNow);
     Need(EntitySets.Inventory(expected).Length<4096,"One inventory slot must remain for the new workplane.");WorldEmpty(expected,context);
     using(var lease=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"]))
     using(var bytes=new OwnedWorkingBytePin(lease.WorkingPath)) {
      OwnedSaveTargets.NoFence(root,(string)binding["copyId"]);DocumentBindings.Check(lease,expected,expiry,context,result,(string)binding["windowContextSha256"]);
      Need(OwnedCopies.Digest(bytes.Stream)==(string)lease.Inspection["workingSha256"],"Working bytes changed under data pin.");
      Frames(expected,context);Bounds(expected,context);
      var computed=new Dictionary<string,object>{{"schemaVersion",1},{"ownedWorkplaneId",packetId},{"operation","create_world_workplane"},{"preparedUtc",prepare?DateTime.UtcNow.ToString("o"):packet["preparedUtc"]},{"expiresUtc",expiry.ToString("o")},{"executionEnabled",false},{"nativeProviderQualified",false},{"arguments",args},
       {"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"nativeObservedPropertiesSha256",EntitySets.ObservedDigest(expected)},{"windowContextSha256",binding["windowContextSha256"]},
       {"requiredBeforeExecution",new[]{"qualified_create_workplane_provider","once_claim_and_owned_fence","declared_autoactivation_then_world","independent_created_frame_and_preserved_entities_verifier","saved_result_reopen","recovery_fault_matrix"}}};
      if(prepare) {
       string path=Artifact(root,"ownedworkplanes",packetId);Directory.CreateDirectory(Path.GetDirectoryName(path));packetSha=NativeJournal.Freeze(path,computed);
       new NativeJournal(root,packetId,packetSha).Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"contextChangeAttempted",false}});
       packet=proofs.Json(path,packetSha);OwnedOperationPlans.Prepared(root,packetId,packetSha,proofs);
      }else Need(EntitySets.Canonical(computed)==EntitySets.Canonical(packet),"Workplane prepared dependencies changed.");
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)binding["windowContextSha256"]);WorldEmpty(context.Observation(),context);
      OwnedSaveTargets.NoFence(root,(string)binding["copyId"]);lease.Recheck();context.ConfirmDocument();Need(DateTime.UtcNow<expiry&&OwnedCopies.Digest(bytes.Stream)==(string)computed["workingSha256"],"Final workplane lifetime/saved bytes changed.");
      if(execute!=null)execute(Clone(packet),Clone(expected),lease,proofs);
      result["state"]=prepare?"owned_workplane_prepared_observed":"owned_workplane_validated_observed";result["ownedWorkplaneId"]=packetId;result["ownedWorkplaneSha256"]=packetSha;result["ownedWorkplane"]=Clone(packet);result["copyId"]=binding["copyId"];result["expiresUtc"]=expiry.ToString("o");
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
