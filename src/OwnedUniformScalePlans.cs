using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Passive coupled preparation only. No native command/selection callbacks.
 internal static class OwnedUniformScalePlans {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static void Fields(Dictionary<string,object> value,params string[] keys){Require(value!=null&&value.Count==keys.Length&&keys.All(value.ContainsKey),"Uniform scale fields differ.");}
  static double Number(object value) {
   Require(value is int||value is long||value is double||value is float||value is decimal,"JSON numeric scale value required.");
   double result=Program.StrictDouble(value);Require(!double.IsNaN(result)&&!double.IsInfinity(result),"Finite scale value required.");return result;
  }
  internal static Dictionary<string,object> Parameters(Dictionary<string,object> value) {
   Fields(value,"factor","centre","coordinateFrame","lengthUnits","mode");
   Require(Equals(value["coordinateFrame"],"world")&&Equals(value["lengthUnits"],"document_raw")&&Equals(value["mode"],"scale_in_place"),"Explicit world/raw/in-place uniform scaling required.");
   double factor=Number(value["factor"]);Require(factor>0&&factor!=1,"Positive non-noop scale factor required; reflection is separate.");
   var centre=value["centre"] as IList;Require(centre!=null&&centre.Count==3,"Explicit finite 3D scale centre required.");
   return new Dictionary<string,object>{{"factor",factor},{"centre",centre.Cast<object>().Select(Number).ToArray()},{"coordinateFrame","world"},{"lengthUnits","document_raw"},{"mode","scale_in_place"}};
  }
  internal static void Validate(Dictionary<string,object> args) {
   Fields(args,"entitySetId","entitySetSha256","bindingId","bindingSha256","parameters");
   foreach(string key in new[]{"entitySetId","entitySetSha256","bindingId","bindingSha256"})Require(EntitySets.Hex(args[key],key.EndsWith("Sha256",StringComparison.Ordinal)?64:32),"Invalid uniform scale proof identity.");
   var parameters=args["parameters"] as Dictionary<string,object>;Require(parameters!=null,"Explicit scale parameters object required.");Parameters(parameters);
  }
  static string Artifact(string root,string folder,object id) {
   Require(EntitySets.Hex(id,32),"Invalid uniform scale artifact ID.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;
  }
  static void NoFence(string root,Dictionary<string,object> binding,OwnedCopies.OwnershipLease lease) {
   OwnedGeometryExportTargets.NoExportFence(root,(string)binding["copyId"]);
   string fence=Path.Combine(Path.GetDirectoryName(lease.WorkingPath),"mutation-fence.json");
   Require(!File.Exists(fence)&&!Directory.Exists(fence),"Unresolved/tampered mutation fence forbids scale preparation.");
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> arguments,DocumentBindings.IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_uniform_scale_preparation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"executionEnabled",false},{"nativeProviderQualified",false}};
   try {
    Require(EntitySets.Hex(id,32),"Invalid scale request ID.");Validate(arguments);
    // Detach all caller-controlled data before any external getter callback.
    var args=new Dictionary<string,object>(arguments);args["parameters"]=Parameters((Dictionary<string,object>)arguments["parameters"]);
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()) {
     var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};
     var validated=DocumentBindings.Load(root,bindingArgs);
     var binding=proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);
     Require(EntitySets.Canonical(validated)==EntitySets.Canonical(binding),"Binding changed during load.");
     var expected=proofs.Json(Artifact(root,"responses",binding["inspectionRequestId"]),(string)binding["inspectionSha256"],16000000);
     var set=proofs.Json(Artifact(root,"entitysets",args["entitySetId"]),(string)args["entitySetSha256"]);EntitySets.ValidatePrepare(set);
     var source=proofs.Json(Artifact(root,"responses",set["inspectionRequestId"]),(string)set["inspectionSha256"],16000000);
     var keys=EntitySets.Keys(set["entityKeys"]);var expiry=new[]{EntitySets.Expiry(set["expiresUtc"]),EntitySets.Expiry(binding["expiresUtc"])}.Min();
     EntitySets.CheckObserved(source,expected,keys,expiry,DateTime.UtcNow);
     Require(EntitySets.ObservedDigest(expected)==(string)binding["observedPropertiesSha256"],"Binding observation differs.");
     foreach(string field in new[]{"contextBefore","contextAfter"})Require(EntitySets.Canonical(expected[field])==EntitySets.Canonical(new[]{0,0}),"World frame and empty selection required.");
     foreach(string field in new[]{"selectionBefore","selectionAfter"})Require(((IList)expected[field]).Count==0,"Empty selection required.");
     using(var lease=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"]))
     using(var workingPin=new OwnedWorkingBytePin(lease.WorkingPath)) {
      // Preparation does not save CAD: pin current saved bytes against writes,
      // without requiring them to equal ownership's historic initial bytes.
      lease.Recheck();NoFence(root,binding,lease);DocumentBindings.Check(lease,expected,expiry,context,result,(string)binding["windowContextSha256"]);
      // A fence may have appeared during the getter reads. Never publish as accepted.
      NoFence(root,binding,lease);
      var packet=new Dictionary<string,object>(args){{"schemaVersion",1},{"ownedScaleId",id},{"operation","uniform_scale_entities"},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",expiry.ToString("o")},{"executionEnabled",false},
       {"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"entityKeys",keys},
       {"setInspectionRequestId",set["inspectionRequestId"]},{"setInspectionSha256",set["inspectionSha256"]},{"bindingInspectionRequestId",binding["inspectionRequestId"]},{"bindingInspectionSha256",binding["inspectionSha256"]},
       {"nativeObservedPropertiesSha256",EntitySets.ObservedDigest(expected)},{"windowContextSha256",binding["windowContextSha256"]},{"lengthUnitsRaw",expected["lengthUnitsRaw"]},{"lengthUnitFactorRaw",expected["lengthUnitFactorRaw"]},
       {"nativeProviderQualified",false},{"requiredBeforeExecution",new[]{"qualified_uniform_scale_recipe","qualified_native_measurements","fresh_owned_context_guard","exact_target_set","primitive_remapping_proof","once_claim_and_mutation_fence","independent_postconditions","saved_result_reopen","recovery_fault_matrix"}}};
      string path=Artifact(root,"ownedscales",id);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,packet);
      result["ownedScaleId"]=id;result["ownedScaleSha256"]=sha;result["localArtifactsMayExist"]=true;
      new NativeJournal(root,id,sha).Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"geometryMutationAttempted",false}});
      proofs.Json(path,sha);OwnedOperationPlans.Prepared(root,id,sha,proofs);
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)binding["windowContextSha256"]);
      NoFence(root,binding,lease);lease.Recheck();context.ConfirmDocument();
      Require(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)binding["windowContextSha256"],"Final scale context/expiry changed.");
      result["state"]="owned_uniform_scale_prepared_observed";result["ownedScale"]=packet;result["guardPasses"]=2;result["sameContextObservationReads"]=4;result["copyId"]=packet["copyId"];result["expiresUtc"]=packet["expiresUtc"];
     }
    }
   }catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args) {
   try {
    var config=Program.ReadConfig();Require(config.IsGeneralCore&&config.ownedUniformScalePlansExperimental,"Owned uniform scale preparation disabled.");Validate(args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
    using(var context=new DocumentBindings.ComContext(config))return Prepare(Program.Root,id,args,context);
   }catch(Exception error){return new Dictionary<string,object>{{"state","owned_uniform_scale_preparation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"executionEnabled",false},{"nativeProviderQualified",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"error",error.Message}};}
  }
 }
}
