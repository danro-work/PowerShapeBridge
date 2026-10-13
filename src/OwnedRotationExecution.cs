using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Internal batch executor. No production queue/COM entrypoint until independent verification is qualified.
 internal static class OwnedRotationExecution {
  internal interface IContext : DocumentBindings.IContext {
   int Composite(int type,int nativeId);
   double[] Bounds(int composite);
   RotationSampleVerifier.Samples Measurements(int composite);
   int Rotate(int composite,int integerDegrees,double[] pointAndUnitDirection);
  }
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static string Artifact(string root,string folder,string id){Require(EntitySets.Hex(id,32),"Invalid rotation identity.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal static void Validate(Dictionary<string,object> args){Require(args.Count==2&&args.ContainsKey("ownedRotationId")&&args.ContainsKey("ownedRotationSha256")&&EntitySets.Hex(args["ownedRotationId"],32)&&EntitySets.Hex(args["ownedRotationSha256"],64),"Exact owned rotation proof required.");}
  static void Box(double[] values){Require(values!=null&&values.Length==6&&values.All(n=>!double.IsNaN(n)&&!double.IsInfinity(n))&&values[0]<=values[1]&&values[2]<=values[3]&&values[4]<=values[5],"Finite paired native world bounds required.");}
  static bool Near(double[] a,double[] b){return a.Length==b.Length&&a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-6).All(equal=>equal);}
  internal static Dictionary<string,object> Packet(string root,Dictionary<string,object> args,OwnedOperationPlans.Proofs proofs) {
   Validate(args);var packet=proofs.Json(Artifact(root,"ownedrotations",(string)args["ownedRotationId"]),(string)args["ownedRotationSha256"]);
   string[] fields={"rotationPlanId","rotationPlanSha256","bindingId","bindingSha256","schemaVersion","ownedRotationId","preparedUtc","expiresUtc","executionEnabled","nativeObservedPropertiesSha256","windowContextSha256","workingSha256","copyId","ownershipSha256"};
   Require(packet.Count==fields.Length&&fields.All(packet.ContainsKey)&&Equals(packet["schemaVersion"],1)&&Equals(packet["ownedRotationId"],args["ownedRotationId"])&&Equals(packet["executionEnabled"],false),"Disabled rotation packet schema/identity differs.");
   foreach(string key in new[]{"copyId","rotationPlanId","bindingId"})Require(EntitySets.Hex(packet[key],32),"Invalid rotation dependency ID.");
   foreach(string key in new[]{"rotationPlanSha256","bindingSha256","nativeObservedPropertiesSha256","windowContextSha256","workingSha256","ownershipSha256"})Require(EntitySets.Hex(packet[key],64),"Invalid rotation dependency SHA.");
   Require(EntitySets.Expiry(packet["preparedUtc"])<=DateTime.UtcNow.AddSeconds(5),"Rotation packet timestamp invalid.");
   OwnedOperationPlans.Prepared(root,(string)args["ownedRotationId"],(string)args["ownedRotationSha256"],proofs);return packet;
  }
  static void Match(Dictionary<string,object> packet,OwnedRotationPlans.Dependencies d,OwnedCopies.OwnershipLease lease) {
   Require(EntitySets.Expiry(packet["expiresUtc"])==d.Expires&&Equals(packet["nativeObservedPropertiesSha256"],EntitySets.ObservedDigest(d.Expected))&&Equals(packet["windowContextSha256"],d.Binding["windowContextSha256"])&&Equals(packet["copyId"],d.Binding["copyId"])&&Equals(packet["ownershipSha256"],d.Binding["ownershipSha256"])&&Equals(packet["workingSha256"],lease.Inspection["workingSha256"]),"Rotation packet dependencies changed.");
  }
  internal static void PreserveOthers(Dictionary<string,object> before,Dictionary<string,object> fresh,int[][] completed) {
   var normalized=Clone(fresh);var expected=EntitySets.Inventory(before);var current=EntitySets.Inventory(normalized);
   foreach(var key in completed) {
    var original=expected.Single(e=>Equals(e["nativeTypeCode"],key[0])&&Equals(e["nativeId"],key[1]));var observed=current.Single(e=>Equals(e["nativeTypeCode"],key[0])&&Equals(e["nativeId"],key[1]));
    // Only geometry of already-called targets may differ; identity/metadata/context and other targets stay pinned.
    if(original.ContainsKey("geometry")){Require(observed.ContainsKey("geometry"),"Target observation disappeared.");observed["geometry"]=original["geometry"];}else observed.Remove("geometry");
   }
   Require(EntitySets.ObservedDigest(before)==EntitySets.ObservedDigest(normalized),"Untargeted observation/context/units changed during rotation.");
  }
  static Dictionary<string,object> Stable(OwnedRotationPlans.Dependencies d,OwnedCopies.OwnershipLease lease,IContext context,Dictionary<string,object> expected,int[][] allowed,Dictionary<string,string> saved) {
   var first=context.Observation();var second=context.Observation();PreserveOthers(expected,first,allowed);PreserveOthers(expected,second,allowed);
   Require(EntitySets.ObservedDigest(first)==EntitySets.ObservedDigest(second),"Observation changed between rotation guard reads.");
   lease.CheckPath((string)((Dictionary<string,object>)second["model"])["path"]);lease.Recheck();context.ConfirmDocument();
   var windows=context.Windows();Require(DocumentBindings.WindowDigest(windows)==(string)d.Binding["windowContextSha256"]&&DateTime.UtcNow<d.Expires,"Rotation window context/lease changed.");var freshSaved=WorkingCopyOpen.SavedHashes(windows);
   Require(saved.Count==freshSaved.Count&&saved.All(pair=>freshSaved.ContainsKey(pair.Key)&&freshSaved[pair.Key]==pair.Value),"Saved document bytes changed during rotation.");return second;
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_rotation_rejected_before_change"},{"operationId",id},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false},{"independentVerificationRequired",true}};
   NativeJournal journal=null;int calls=0;string fence=null;Dictionary<string,object> sourceProof=null;
   try {
    Require(EntitySets.Hex(id,32),"Invalid execution request ID.");
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()) {
     var packet=Packet(root,args,proofs);var parameters=new[]{"rotationPlanId","rotationPlanSha256","bindingId","bindingSha256"}.ToDictionary(key=>key,key=>packet[key]);var d=new OwnedRotationPlans.Dependencies(root,parameters,proofs);
     var axis=OwnedRotationPlans.Axis((Dictionary<string,object>)d.Plan["parameters"]);int angle=Program.StrictInt(((Dictionary<string,object>)d.Plan["parameters"])["angleDegrees"]);
     sourceProof=new Dictionary<string,object>{{"requestId",d.Binding["inspectionRequestId"]},{"sha256",d.Binding["inspectionSha256"]}};
     using(var lease=OwnedCopies.Hold(root,(string)packet["copyId"],(string)packet["ownershipSha256"])) {
      Match(packet,d,lease);fence=OwnedTranslation.FencePath(root,(string)packet["copyId"]);string claim=Path.Combine(root,"rotationplans",(string)packet["rotationPlanId"]+".execution-once.json");OwnedCopies.NoReparse(claim);
      Require(!File.Exists(fence)&&!File.Exists(claim),"Rotation copy fenced or plan already claimed; no replay.");d.Check(lease,context,result);
      var targets=d.Keys.Select(key=>{int composite=context.Composite(key[0],key[1]);var bounds=context.Bounds(composite);Box(bounds);return new Dictionary<string,object>{{"entityKey",key},{"compositeId",composite},{"boundsBeforeWorldRawPaired",bounds}};}).ToArray();
      Require(targets.Select(target=>target["compositeId"]).Distinct().Count()==targets.Length,"Aliased composite targets forbidden.");var windows=context.Windows();var saved=WorkingCopyOpen.SavedHashes(windows);
      var provenance=RotationMeasurementRecords.Provenance(packet,d);
      var captured=targets.Select(target=>{var samples=context.Measurements((int)target["compositeId"]);RotationSampleVerifier.Validate(samples);var key=(int[])target["entityKey"];Require(samples.Type==key[0]&&samples.NativeId==key[1]&&Near(samples.BoundsPaired,(double[])target["boundsBeforeWorldRawPaired"]),"Before-measurement target/bounds differ.");return samples;}).ToArray();
      d.Check(lease,context,result);
      result["localArtifactsMayExist"]=true;
      for(int index=0;index<targets.Length;index++){string measurementSha=RotationMeasurementRecords.Freeze(root,id,index,"before",provenance,captured[index]);targets[index]["beforeMeasurementSha256"]=measurementSha;RotationMeasurementRecords.Read(root,id,index,"before",measurementSha,provenance,proofs);}
      var plan=new Dictionary<string,object>(parameters){{"schemaVersion",2},{"operationId",id},{"operation","rotate_owned_entities"},{"ownedRotationId",args["ownedRotationId"]},{"ownedRotationSha256",args["ownedRotationSha256"]},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"workingSha256",packet["workingSha256"]},{"inspectionRequestId",d.Binding["inspectionRequestId"]},{"inspectionSha256",d.Binding["inspectionSha256"]},{"windowContextSha256",packet["windowContextSha256"]},{"measurementProvenance",provenance},{"angleDegrees",angle},{"nativeAxis",axis},{"targets",targets},{"beforeWindows",windows},{"savedFileHashesBefore",saved},{"createdUtc",DateTime.UtcNow.ToString("o")},{"qualification","Internal executor; native sign/frame and independent geometry verification not qualified"}};
      string path=Artifact(root,"rotationexecutions",id);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,plan);result["plan"]=plan;result["planSha256"]=sha;result["copyId"]=packet["copyId"];result["localArtifactsMayExist"]=true;
      journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"operation","rotate_owned_entities"},{"targetCount",targets.Length}});
      string fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"automaticReplayForbidden",true}});result["executionFenceSha256"]=fenceSha;proofs.Json(fence,fenceSha);
      string claimSha=NativeJournal.Freeze(claim,new{operationId=id,planSha256=sha,ownedRotationId=args["ownedRotationId"],copyId=packet["copyId"],automaticReplayForbidden=true});proofs.Json(claim,claimSha);journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"targetCount",targets.Length}});d.Check(lease,context,result);
      OwnedOperationPlans.Prepared(root,(string)packet["rotationPlanId"],(string)packet["rotationPlanSha256"],proofs);OwnedOperationPlans.Prepared(root,(string)args["ownedRotationId"],(string)args["ownedRotationSha256"],proofs);
      string commands=Path.Combine(root,"rotationcommands",id);Directory.CreateDirectory(commands);var current=d.Expected;var records=new List<Dictionary<string,object>>();
      for(int index=0;index<targets.Length;index++) {
       var snapshot=Stable(d,lease,context,current,new int[0][],saved);var key=d.Keys[index];int composite=context.Composite(key[0],key[1]);var bounds=context.Bounds(composite);Box(bounds);
       Require(Equals(composite,targets[index]["compositeId"])&&Near(bounds,(double[])targets[index]["boundsBeforeWorldRawPaired"]),"Next rotation target/mapping/bounds changed.");
       context.ConfirmDocument();Require(DateTime.UtcNow<d.Expires&&DocumentBindings.WindowDigest(context.Windows())==(string)d.Binding["windowContextSha256"],"Context changed immediately before rotation.");
       string prefix=index.ToString("D6");string started=NativeJournal.Freeze(Path.Combine(commands,prefix+".started.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"index",index},{"entityKey",key},{"compositeId",composite},{"angleDegrees",angle},{"nativeAxis",axis},{"beforeObservationSha256",EntitySets.ObservedDigest(snapshot)}});
       if(index==0)journal.Append("command_started",new Dictionary<string,object>{{"command","direct RotateItem / explicit batch"},{"targetCount",targets.Length}});
       // Artifact writes may take time. Recheck after publication, immediately before the callback.
       var finalBefore=Stable(d,lease,context,current,new int[0][],saved);
       Require(EntitySets.ObservedDigest(finalBefore)==EntitySets.ObservedDigest(snapshot)&&context.Composite(key[0],key[1])==composite&&Near(context.Bounds(composite),bounds),"Rotation target/context changed after command-start publication.");
       context.ConfirmDocument();Require(DateTime.UtcNow<d.Expires&&DocumentBindings.WindowDigest(context.Windows())==(string)d.Binding["windowContextSha256"],"Final rotation context/lease changed.");
       var beforeSamples=RotationMeasurementRecords.Read(root,id,index,"before",(string)targets[index]["beforeMeasurementSha256"],provenance,proofs);
       Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(beforeSamples))==EntitySets.Canonical(RotationMeasurementRecords.Encode(context.Measurements(composite))),"Native measurements changed before target rotation.");
       var measuredGuard=Stable(d,lease,context,current,new int[0][],saved);Require(EntitySets.ObservedDigest(measuredGuard)==EntitySets.ObservedDigest(snapshot)&&context.Composite(key[0],key[1])==composite&&Near(context.Bounds(composite),bounds),"Context changed while rereading measurements.");
       context.ConfirmDocument();Require(DateTime.UtcNow<d.Expires&&DocumentBindings.WindowDigest(context.Windows())==(string)d.Binding["windowContextSha256"],"Measurement read exhausted final rotation context/lease.");
       calls++;result["geometryMutationAttempted"]=true;int code=context.Rotate(composite,angle,axis.ToArray());string returned=NativeJournal.Freeze(Path.Combine(commands,prefix+".returned.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"index",index},{"startedSha256",started},{"returnCode",code},{"timestampUtc",DateTime.UtcNow.ToString("o")}});
       Require(code==1,"RotateItem returned non-success; batch stops without retry/inverse.");
       var after=Stable(d,lease,context,current,new[]{key},saved);Require(context.Composite(key[0],key[1])==composite,"Rotated target mapping changed; independent diagnosis required.");var afterBounds=context.Bounds(composite);Box(afterBounds);current=after;
       var afterSamples=context.Measurements(composite);RotationSampleVerifier.Validate(afterSamples);Require(afterSamples.Type==key[0]&&afterSamples.NativeId==key[1]&&Near(afterSamples.BoundsPaired,afterBounds),"After-measurement target/bounds differ.");string afterSha=RotationMeasurementRecords.Freeze(root,id,index,"after",provenance,afterSamples);RotationMeasurementRecords.Read(root,id,index,"after",afterSha,provenance,proofs);
       string observation=NativeJournal.Freeze(Path.Combine(commands,prefix+".observed.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"index",index},{"returnedSha256",returned},{"afterObservationSha256",EntitySets.ObservedDigest(after)},{"boundsAfterWorldRawPaired",afterBounds},{"geometryVerified",false}});
       records.Add(new Dictionary<string,object>{{"index",index},{"entityKey",key},{"beforeMeasurementSha256",targets[index]["beforeMeasurementSha256"]},{"afterMeasurementSha256",afterSha},{"startedSha256",started},{"returnedSha256",returned},{"observedSha256",observation},{"returnCode",code}});
      }
      journal.Append("command_returned",new Dictionary<string,object>{{"returnCode",1},{"commandCalls",calls},{"allTargetsReturnedSuccess",true}});
      string summary=NativeJournal.Freeze(Path.Combine(commands,"summary.json"),new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"commands",records},{"geometryVerified",false}});
      journal.Append("executed_unverified",new Dictionary<string,object>{{"commandCalls",calls},{"summarySha256",summary},{"independentVerificationRequired",true}});
      result["state"]="owned_rotation_executed_unverified";result["commandSummarySha256"]=summary;result["returnCode"]=1;result["targetCount"]=targets.Length;result["geometryVerified"]=false;result["savedFilesUnchanged"]=true;
     }
    }
   }catch(Exception error) {
    result["error"]=error.Message;bool uncertain=calls>0||journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State);result["state"]=uncertain?"owned_rotation_outcome_unknown":"owned_rotation_rejected_before_change";
    if(journal!=null)try{if(new[]{"prepared","execution_claimed"}.Contains(journal.State))journal.Append("rejected_before_change",new Dictionary<string,object>{{"geometryMutationAttempted",false},{"evidence",new[]{sourceProof}}});else if(new[]{"command_started","command_returned"}.Contains(journal.State))journal.Append("outcome_unknown",new Dictionary<string,object>{{"commandCallsAttempted",calls},{"error",error.Message}});}catch(Exception journalError){result["journalError"]=journalError.Message;}
   }
   result["commandCallsAttempted"]=calls;result["copyFenced"]=fence!=null&&File.Exists(fence);if(journal!=null)result["recordedJournalState"]=journal.State;result["timestampUtc"]=DateTime.UtcNow.ToString("o");result["recoveryInstructions"]="Read original journal and per-target records; do not repeat/continue the batch, inverse, save or remove the fence automatically.";return result;
  }
 }
}
