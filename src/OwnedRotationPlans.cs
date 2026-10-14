using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Passive held-context ownership guard. No rotation, selection, save or execution callback.
 internal static class OwnedRotationPlans {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static string Artifact(string root,string folder,object id){Require(EntitySets.Hex(id,32),"Invalid rotation artifact ID.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;}
  static void Fields(Dictionary<string,object> value,params string[] fields){Require(value.Count==fields.Length&&fields.All(value.ContainsKey),"Rotation proof fields differ.");}
  static double Number(object value){Require(value is int||value is long||value is double||value is decimal||value is float,"Numeric rotation value required.");double result=Program.StrictDouble(value);Require(!double.IsNaN(result)&&!double.IsInfinity(result)&&Math.Abs(result)<=1000000,"Rotation value out of range.");return result;}
  static double[] Vector(object value){var array=value as IList;Require(array!=null&&array.Count==3,"Explicit 3D rotation vector required.");return array.Cast<object>().Select(Number).ToArray();}
  internal static double[] Axis(Dictionary<string,object> parameters) {
   Fields(parameters,"angleDegrees","origin","direction","coordinateFrame","lengthUnits");Require(Equals(parameters["coordinateFrame"],"world")&&Equals(parameters["lengthUnits"],"document_raw"),"Explicit world/document_raw rotation required.");
   double angle=Number(parameters["angleDegrees"]);Require(angle==Math.Truncate(angle)&&angle%360!=0,"Integer non-noop rotation required.");var origin=Vector(parameters["origin"]);var direction=Vector(parameters["direction"]);double maximum=direction.Max(value=>Math.Abs(value));Require(maximum>0,"Nonzero rotation axis required.");var scaled=direction.Select(value=>value/maximum).ToArray();double norm=Math.Sqrt(scaled.Sum(value=>value*value));return origin.Concat(scaled.Select(value=>value/norm)).ToArray();
  }
  internal static void Validate(Dictionary<string,object> args){Fields(args,"rotationPlanId","rotationPlanSha256","bindingId","bindingSha256");foreach(var pair in args)Require(EntitySets.Hex(pair.Value,pair.Key.EndsWith("Sha256",StringComparison.Ordinal)?64:32),"Invalid owned rotation proof identity.");}
  internal sealed class Dependencies {
   internal readonly Dictionary<string,object> Plan,Binding,Expected;internal readonly int[][] Keys;internal readonly DateTime Expires;
   internal Dependencies(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs) {
    Validate(args);Plan=proofs.Json(Artifact(root,"rotationplans",args["rotationPlanId"]),(string)args["rotationPlanSha256"]);
    Fields(Plan,"schemaVersion","operationId","operation","preparedUtc","expiresUtc","executionEnabled","parameters","entitySet","sourceIdentityDigest","sourceObservedDigest","entityKeys","nativeMethod","nativeAngleType","nativeAxis","lengthUnitsRaw","lengthUnitFactorRaw","requiredBeforeExecution","qualification");
    Require(Equals(Plan["schemaVersion"],1)&&Equals(Plan["operationId"],args["rotationPlanId"])&&Equals(Plan["operation"],"rotate_entities")&&Equals(Plan["executionEnabled"],false)&&Equals(Plan["nativeMethod"],"IPshapeModelDoc.RotateItem")&&Equals(Plan["nativeAngleType"],"long (integer degrees)"),"Rotation plan policy differs.");
    Require(EntitySets.Hex(Plan["sourceIdentityDigest"],64)&&EntitySets.Hex(Plan["sourceObservedDigest"],64)&&EntitySets.Canonical(Plan["requiredBeforeExecution"])==EntitySets.Canonical(new[]{"qualified_native_rotation_executor","owned_working_copy","independent_checkpoint","world_frame_guard","fresh_same_context_guard","independent_postconditions","recovery_fault_matrix"}),"Rotation plan requirements differ.");
    var axis=Axis((Dictionary<string,object>)Plan["parameters"]);var recorded=(Plan["nativeAxis"] as IList);Require(recorded!=null&&recorded.Count==6&&axis.Zip(recorded.Cast<object>().Select(Number),(a,b)=>Math.Abs(a-b)<=1e-12).All(equal=>equal),"Recorded normalized axis differs.");
    OwnedOperationPlans.Prepared(root,(string)args["rotationPlanId"],(string)args["rotationPlanSha256"],proofs);
    var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};var loaded=DocumentBindings.Load(root,bindingArgs);Binding=proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);Require(EntitySets.Canonical(loaded)==EntitySets.Canonical(Binding),"Rotation binding changed during load.");
    Expected=proofs.Json(Artifact(root,"responses",Binding["inspectionRequestId"]),(string)Binding["inspectionSha256"],16000000);
    var setArgs=(Dictionary<string,object>)Plan["entitySet"];Fields(setArgs,"entitySetId","entitySetSha256");EntitySets.ValidateHandle(setArgs);
    var set=proofs.Json(Artifact(root,"entitysets",setArgs["entitySetId"]),(string)setArgs["entitySetSha256"]);EntitySets.ValidatePrepare(set);
    var source=proofs.Json(Artifact(root,"responses",set["inspectionRequestId"]),(string)set["inspectionSha256"],16000000);Keys=EntitySets.Keys(set["entityKeys"]);
    Require(EntitySets.Canonical(set["entityKeys"])==EntitySets.Canonical(Plan["entityKeys"]),"Rotation target keys differ from set.");
    var setExpiry=EntitySets.Expiry(set["expiresUtc"]);Expires=new[]{setExpiry,EntitySets.Expiry(Binding["expiresUtc"])}.Min();EntitySets.CheckObserved(source,Expected,Keys,Expires,DateTime.UtcNow);
    Require(EntitySets.Expiry(Plan["expiresUtc"])==setExpiry&&EntitySets.Expiry(Plan["preparedUtc"])>=EntitySets.Expiry(source["timestampUtc"])&&EntitySets.Expiry(Plan["preparedUtc"])<=DateTime.UtcNow.AddSeconds(5),"Rotation plan lifetime differs.");
    Require(EntitySets.ObservedDigest(Expected)==(string)Binding["observedPropertiesSha256"]&&EntitySets.Canonical(Plan["lengthUnitsRaw"])==EntitySets.Canonical(source["lengthUnitsRaw"])&&EntitySets.Canonical(Plan["lengthUnitFactorRaw"])==EntitySets.Canonical(source["lengthUnitFactorRaw"]),"Rotation source/binding/units differ.");
    foreach(string key in new[]{"contextBefore","contextAfter"})Require(EntitySets.Canonical(Expected[key])==EntitySets.Canonical(new[]{0,0}),"World workplane and empty selection required.");
    foreach(string key in new[]{"selectionBefore","selectionAfter"})Require(((IList)Expected[key]).Count==0,"Empty selection required.");
   }
   internal void Check(OwnedCopies.OwnershipLease lease,DocumentBindings.IContext context,Dictionary<string,object> result){EntitySets.CheckObserved(Expected,Expected,Keys,Expires,DateTime.UtcNow);DocumentBindings.Check(lease,Expected,Expires,context,result,(string)Binding["windowContextSha256"]);}
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args,DocumentBindings.IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_rotation_preparation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"executionEnabled",false}};
   try {
    Require(EntitySets.Hex(id,32),"Invalid request identity.");
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()) {
     var dependencies=new Dependencies(root,args,proofs);
     using(var lease=OwnedCopies.Hold(root,(string)dependencies.Binding["copyId"],(string)dependencies.Binding["ownershipSha256"])) {
      OwnedGeometryExportTargets.NoExportFence(root,(string)dependencies.Binding["copyId"]);Require(!File.Exists(Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"mutation-fence.json")),"Working copy is fenced; rotation preparation forbidden.");dependencies.Check(lease,context,result);
      var packet=new Dictionary<string,object>(args){{"schemaVersion",1},{"ownedRotationId",id},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",dependencies.Expires.ToString("o")},{"executionEnabled",false},{"nativeObservedPropertiesSha256",EntitySets.ObservedDigest(dependencies.Expected)},{"windowContextSha256",dependencies.Binding["windowContextSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"copyId",dependencies.Binding["copyId"]},{"ownershipSha256",dependencies.Binding["ownershipSha256"]}};
      string path=Artifact(root,"ownedrotations",id);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,packet);result["ownedRotationId"]=id;result["ownedRotationSha256"]=sha;result["localArtifactsMayExist"]=true;
      var journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"geometryMutationAttempted",false}});
      dependencies.Check(lease,context,result);OwnedOperationPlans.Prepared(root,(string)args["rotationPlanId"],(string)args["rotationPlanSha256"],proofs);OwnedOperationPlans.Prepared(root,id,sha,proofs);lease.Recheck();context.ConfirmDocument();Require(DateTime.UtcNow<dependencies.Expires&&DocumentBindings.WindowDigest(context.Windows())==(string)dependencies.Binding["windowContextSha256"],"Rotation context/expiry changed.");
      result["state"]="owned_rotation_prepared_observed";result["ownedRotation"]=packet;result["expiresUtc"]=packet["expiresUtc"];result["guardPasses"]=2;result["sameContextObservationReads"]=4;result["copyId"]=packet["copyId"];
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args) {
   try {
    var config=Program.ReadConfig();Require(config.IsGeneralCore&&config.ownedRotationPlansExperimental,"Owned rotation preparation disabled.");Validate(args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
    using(var context=new DocumentBindings.ComContext(config))return Prepare(Program.Root,id,args,context);
   }catch(Exception error){return new Dictionary<string,object>{{"state","owned_rotation_preparation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"executionEnabled",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"error",error.Message}};}
  }
 }
}
