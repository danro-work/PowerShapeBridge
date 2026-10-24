using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Independent read context only. Generic verification retains fences; the native-backed
 // one-SOLID WORLD normalized-axis quarter-turn entry point can release only its verified matching fence.
 internal static class OwnedRotationVerification {
  internal interface IContext : DocumentBindings.IContext {
   int Composite(int type,int id);
   double[] Bounds(int composite);
   RotationSampleVerifier.Samples Measurements(int composite);
  }
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static void Fields(Dictionary<string,object> value,params string[] fields){Require(value.Count==fields.Length&&fields.All(value.ContainsKey),"Verification proof fields differ.");}
  static string Artifact(string root,string folder,string id){Require(EntitySets.Hex(id,32),"Fixed proof identity required.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  static object[] Values(object value){var list=value as IList;Require(list!=null,"Explicit proof array required.");return list.Cast<object>().ToArray();}
  static double[] Numbers(object value){return Values(value).Select(v=>{Require(v is int||v is long||v is double||v is float||v is decimal,"Native numeric evidence required.");double number=Program.StrictDouble(v);Require(!double.IsNaN(number)&&!double.IsInfinity(number),"Finite numeric evidence required.");return number;}).ToArray();}
  internal static void Validate(Dictionary<string,object> args) {
   string[] ids={"executionRequestId","inspectionRequestId","listingRequestId"},shas={"executionReceiptSha256","inspectionSha256","listingSha256"};
   Require(args.Count==9&&ids.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],32))&&shas.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],64))&&args.ContainsKey("windowNumber")&&args["windowNumber"] is int&&args.ContainsKey("modelId")&&args["modelId"] is int&&args.ContainsKey("expiresUtc"),"Exact independent rotation references required.");EntitySets.Expiry(args["expiresUtc"]);
  }
  internal static Dictionary<string,object> Verify(string root,string verificationId,Dictionary<string,object> args,IContext context) {
   return VerifyCore(root,verificationId,args,context,0);
  }
  // First bounded live qualification. Other angles/axes/types retain the unqualified fence.
  internal static Dictionary<string,object> VerifyQualifiedSolidQuarterTurn(string root,string verificationId,Dictionary<string,object> args,SolidRotationContext.ReadContext context) {
   return VerifyCore(root,verificationId,args,context,1);
  }
  internal static Dictionary<string,object> VerifyQualifiedSolidRotation(string root,string verificationId,Dictionary<string,object> args,SolidRotationContext.ReadContext context){return VerifyCore(root,verificationId,args,context,2);}
  internal static bool NativeRotationScope(IContext context,Dictionary<string,object> plan,int[][] keys,Dictionary<string,object> expected,bool general){if(!general)return NativeQuarterTurnScope(context,plan,keys,expected);var native=context as SolidRotationContext.ReadContext;return native!=null&&native.IsNativeProvider&&OwnedSolidQuarterTurn.SupportedRotation(Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]))&&keys.Length==1&&keys[0][0]==40&&EntitySets.Inventory(expected).Length==1&&Equals(expected["lengthUnitsRaw"],"mm")&&Program.StrictDouble(expected["lengthUnitFactorRaw"])==1;}
  internal static bool NativeQuarterTurnScope(IContext context,Dictionary<string,object> plan,int[][] keys,Dictionary<string,object> expected) {
   var native=context as SolidRotationContext.ReadContext;
   return native!=null&&native.IsNativeProvider&&(Equals(plan["angleDegrees"],90)||Equals(plan["angleDegrees"],-90))&&OwnedSolidQuarterTurn.Supported((int)plan["angleDegrees"],Numbers(plan["nativeAxis"]))&&keys.Length==1&&keys[0][0]==40&&EntitySets.Inventory(expected).Length==1&&Equals(expected["lengthUnitsRaw"],"mm")&&Program.StrictDouble(expected["lengthUnitFactorRaw"])==1;
  }
  static Dictionary<string,object> VerifyCore(string root,string verificationId,Dictionary<string,object> args,IContext context,int qualificationKind) {
   bool qualifyQuarterTurn=qualificationKind==1,qualifyGeneral=qualificationKind==2;
   var result=new Dictionary<string,object>{{"state","owned_rotation_verification_rejected"},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false},{"copyFenceReleased",false},{"nativeConventionQualified",false},{"verificationId",verificationId}};
   OwnedOperationPlans.Proofs fenceProofs=null;
   try {
    Validate(args);Require(EntitySets.Hex(verificationId,32),"Verification identity required.");
    if(qualifyGeneral){var original=EntitySets.Receipt(root,(string)args["executionRequestId"],(string)args["executionReceiptSha256"]);if(Equals(original["state"],"owned_rotation_outcome_unknown"))return ReturnedSolidRotationVerification.Verify(root,verificationId,args,(SolidRotationContext.ReadContext)context);}
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()) {
     string id=(string)args["executionRequestId"];var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["executionReceiptSha256"],16000000);
     Require(Equals(receipt["operationId"],id)&&Equals(receipt["state"],"owned_rotation_executed_unverified")&&Equals(receipt["returnCode"],1)&&Equals(receipt["geometryVerified"],false)&&Equals(receipt["geometryMutationAttempted"],true)&&Equals(receipt["contextChangeAttempted"],false)&&Equals(receipt["cadSaveAttempted"],false),"Complete unverified execution receipt required; not interrupted/partial batch.");
     var plan=proofs.Json(Artifact(root,"rotationexecutions",id),(string)receipt["planSha256"],16000000);Require(Equals(plan["schemaVersion"],2)&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"rotate_owned_entities")&&EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"]),"Pinned measured execution plan differs.");
     var targets=Values(plan["targets"]).Cast<Dictionary<string,object>>().ToArray();Require(targets.Length>0&&targets.Length<=128,"Bounded targets required.");var keys=targets.Select(target=>EntitySets.Keys(new[]{target["entityKey"]}).Single()).ToArray();Require(keys.Select(k=>k[0]+":"+k[1]).Distinct().Count()==keys.Length&&targets.Select(t=>t["compositeId"]).Distinct().Count()==targets.Length,"Duplicate execution targets.");
     var journal=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]);Require(journal.CommandReturnCode==1&&journal.State=="executed_unverified"&&Equals(journal.CommandReturnedDetails["commandCalls"],targets.Length)&&Equals(journal.CommandReturnedDetails["allTargetsReturnedSuccess"],true),"Authoritative journal does not confirm all API returns.");
     var summary=proofs.Json(Path.Combine(root,"rotationcommands",id,"summary.json"),(string)receipt["commandSummarySha256"],16000000);Require(Equals(summary["operationId"],id)&&Equals(summary["planSha256"],receipt["planSha256"])&&Equals(summary["geometryVerified"],false)&&Equals(journal.LastDetails["summarySha256"],receipt["commandSummarySha256"]),"Journal/summary linkage differs.");var commands=Values(summary["commands"]).Cast<Dictionary<string,object>>().ToArray();Require(commands.Length==targets.Length,"Missing command proofs.");
     var before=proofs.Json(Artifact(root,"responses",(string)plan["inspectionRequestId"]),(string)plan["inspectionSha256"],16000000);var expected=proofs.Json(Artifact(root,"responses",(string)args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Artifact(root,"responses",(string)args["listingRequestId"]),(string)args["listingSha256"],16000000);var expiry=EntitySets.Expiry(args["expiresUtc"]);
     EntitySets.CheckObserved(expected,expected,keys,expiry,DateTime.UtcNow);Require(EntitySets.Expiry(expected["timestampUtc"])>journal.CommandReturnedUtc&&EntitySets.Expiry(listing["timestampUtc"])>journal.CommandReturnedUtc&&expiry<=EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300)&&Equals(listing["state"],"documents_listed_verified"),"Independent reads must follow aggregate command return.");
     var active=(Dictionary<string,object>)listing["activeWindow"];Require(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"])&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Fresh active listing differs.");OwnedRotationExecution.PreserveOthers(before,expected,keys);
     var provenance=(Dictionary<string,object>)plan["measurementProvenance"];Require(Equals(provenance["copyId"],plan["copyId"])&&Equals(provenance["ownershipSha256"],plan["ownershipSha256"])&&Equals(provenance["workingSha256"],plan["workingSha256"]),"Measurement owner differs.");
     using(var lease=OwnedCopies.Hold(root,(string)plan["copyId"],(string)plan["ownershipSha256"])) {
      Require(Equals(lease.Inspection["workingSha256"],plan["workingSha256"]),"Saved working bytes changed.");string fencePath=OwnedTranslation.FencePath(root,(string)plan["copyId"]);fenceProofs=new OwnedOperationPlans.Proofs();var fence=fenceProofs.Json(fencePath,(string)receipt["executionFenceSha256"]);Require(Equals(fence["operationId"],id)&&Equals(fence["planSha256"],receipt["planSha256"])&&Equals(fence["copyId"],plan["copyId"])&&Equals(fence["ownershipSha256"],plan["ownershipSha256"])&&Equals(fence["automaticReplayForbidden"],true),"Original fence differs.");
      var claim=proofs.Json(Path.Combine(root,"rotationplans",(string)plan["rotationPlanId"]+".execution-once.json"),null);Require(Equals(claim["operationId"],id)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["copyId"],plan["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Original one-use claim differs.");
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);
      var measuredTargets=new List<Dictionary<string,object>>();
      for(int index=0;index<targets.Length;index++) {
       var command=commands[index];Require(Equals(command["index"],index)&&EntitySets.Canonical(command["entityKey"])==EntitySets.Canonical(targets[index]["entityKey"])&&Equals(command["beforeMeasurementSha256"],targets[index]["beforeMeasurementSha256"]),"Command target/order differs.");
       string prefix=Path.Combine(root,"rotationcommands",id,index.ToString("D6"));var started=proofs.Json(prefix+".started.json",(string)command["startedSha256"]);var returned=proofs.Json(prefix+".returned.json",(string)command["returnedSha256"]);var observed=proofs.Json(prefix+".observed.json",(string)command["observedSha256"]);
       Require(Equals(started["operationId"],id)&&Equals(started["index"],index)&&Equals(started["planSha256"],receipt["planSha256"])&&EntitySets.Canonical(started["entityKey"])==EntitySets.Canonical(targets[index]["entityKey"])&&Equals(started["compositeId"],targets[index]["compositeId"])&&Equals(started["angleDegrees"],plan["angleDegrees"])&&EntitySets.Canonical(started["nativeAxis"])==EntitySets.Canonical(plan["nativeAxis"]),"Started command differs from frozen plan.");
       Require(Equals(returned["operationId"],id)&&Equals(returned["index"],index)&&Equals(returned["planSha256"],receipt["planSha256"])&&Equals(returned["startedSha256"],command["startedSha256"])&&Equals(returned["returnCode"],1)&&Equals(command["returnCode"],1)&&Equals(observed["operationId"],id)&&Equals(observed["index"],index)&&Equals(observed["planSha256"],receipt["planSha256"])&&Equals(observed["returnedSha256"],command["returnedSha256"]),"Per-target return/observation proof differs.");
       var original=RotationMeasurementRecords.Read(root,id,index,"before",(string)command["beforeMeasurementSha256"],provenance,proofs);var recordedAfter=RotationMeasurementRecords.Read(root,id,index,"after",(string)command["afterMeasurementSha256"],provenance,proofs);Require(original.Type==keys[index][0]&&original.NativeId==keys[index][1]&&recordedAfter.Type==original.Type&&recordedAfter.NativeId==original.NativeId,"Measurement target differs.");
       var originalRecord=proofs.Json(RotationMeasurementRecords.PathFor(root,id,index,"before"),(string)command["beforeMeasurementSha256"],16000000);var afterRecord=proofs.Json(RotationMeasurementRecords.PathFor(root,id,index,"after"),(string)command["afterMeasurementSha256"],16000000);
       Require(EntitySets.Expiry(originalRecord["recordedUtc"])<=EntitySets.Expiry(plan["createdUtc"])&&EntitySets.Expiry(afterRecord["recordedUtc"])>=EntitySets.Expiry(returned["timestampUtc"]),"Before/after measurement chronology differs.");
       int composite=context.Composite(keys[index][0],keys[index][1]);Require(Equals(composite,targets[index]["compositeId"]),"Fresh mapping changed.");var first=context.Measurements(composite);var second=context.Measurements(composite);Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(first))==EntitySets.Canonical(RotationMeasurementRecords.Encode(second)),"Fresh independent samples changed during reads.");
       Require(Numbers(observed["boundsAfterWorldRawPaired"]).SequenceEqual(recordedAfter.BoundsPaired)&&context.Bounds(composite).SequenceEqual(second.BoundsPaired),"Measurement/direct world bounds differ.");
       if((qualifyQuarterTurn||qualifyGeneral)&&NativeRotationScope(context,plan,keys,expected,qualifyGeneral)&&(qualifyGeneral||OwnedSolidQuarterTurn.ScopeFor(Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]))==OwnedSolidQuarterTurn.ObliqueScope)){
        OwnedSolidQuarterTurn.VerifySolidSamples(original,recordedAfter,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),qualifyGeneral);OwnedSolidQuarterTurn.VerifySolidSamples(original,second,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),qualifyGeneral);result["numericVerificationProfile"]=OwnedSolidQuarterTurn.NumericProfileForMode(qualifyGeneral);
       }else {RotationSampleVerifier.Verify(original,recordedAfter,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),1e-6,1e-9);RotationSampleVerifier.Verify(original,second,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),1e-6,1e-9);}
       Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(recordedAfter))==EntitySets.Canonical(RotationMeasurementRecords.Encode(second)),"Recorded and independent samples differ.");
       measuredTargets.Add(new Dictionary<string,object>{{"index",index},{"entityKey",keys[index]},{"compositeId",composite},{"beforeMeasurementSha256",command["beforeMeasurementSha256"]},{"recordedAfterMeasurementSha256",command["afterMeasurementSha256"]},{"freshSamples",RotationMeasurementRecords.Encode(second)}});
      }
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);var saved=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["beforeWindows"]);var originalSaved=(Dictionary<string,object>)plan["savedFileHashesBefore"];Require(saved.Count==originalSaved.Count&&saved.All(pair=>originalSaved.ContainsKey(pair.Key)&&Equals(originalSaved[pair.Key],pair.Value)),"Saved documents changed.");lease.Recheck();context.ConfirmDocument();
      Require(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Verification context/expiry changed before publication.");
      var diagnostic=new Dictionary<string,object>{{"schemaVersion",1},{"verificationId",verificationId},{"executionRequestId",id},{"executionReceiptSha256",args["executionReceiptSha256"]},{"planSha256",receipt["planSha256"]},{"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},{"listingRequestId",args["listingRequestId"]},{"listingSha256",args["listingSha256"]},{"targetCount",targets.Length},{"measuredTargets",measuredTargets},{"state","observed_rotation_samples_match_unqualified"},{"nativeConventionQualified",false},{"copyFenceReleased",false},{"qualification","Observed anchored samples/invariants/bounds; no full topology or qualified native convention"}};
      if(result.ContainsKey("numericVerificationProfile"))diagnostic["numericVerificationProfile"]=result["numericVerificationProfile"];
      string path=Artifact(root,"rotationverifications",verificationId);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,diagnostic);result["state"]=diagnostic["state"];result["diagnosticSha256"]=sha;result["targetCount"]=targets.Length;result["samplePostconditionsPassed"]=true;result["executionRequestId"]=id;result["recordedJournalState"]=journal.State;
      if(qualifyQuarterTurn||qualifyGeneral) {
       Require(NativeRotationScope(context,plan,keys,expected,qualifyGeneral),"Outside the one-SOLID signed WORLD normalized-axis quarter-turn/mm scope.");
       Require(receipt.ContainsKey("solidWitnessBundleSha256")&&EntitySets.Hex(receipt["solidWitnessBundleSha256"],64),"Pinned native solid witness bundle required.");
       var bundle=proofs.Json(Artifact(root,"solidrotationdescriptors",(string)plan["ownedRotationId"]),(string)receipt["solidWitnessBundleSha256"],16000000);
       foreach(string key in new[]{"ownedRotationId","ownedRotationSha256","bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})Require(Equals(bundle[key],plan[key]),"Qualified witness bundle differs from original execution: "+key);
       Require(EntitySets.Canonical(bundle["descriptor"])==((SolidRotationContext.ReadContext)context).PinnedDescriptor,"Qualified reader descriptor differs.");
       var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
       journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"diagnosticSha256",sha},{"verificationScope",qualifyGeneral?"one SOLID bounded integer WORLD rotation in mm; explicit numeric profile and pinned samples/invariants/bounds; no full topology":"one SOLID signed WORLD normalized-axis quarter-turn with explicit pivot in mm; pinned face UV positions/normals, COG/invariants/bounds, ownership/files/context; no full topology"}});
       fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fencePath,(string)receipt["executionFenceSha256"],65536);File.Delete(fencePath);
       string scope=OwnedSolidQuarterTurn.ScopeForMode((int)plan["angleDegrees"],Numbers(plan["nativeAxis"]),qualifyGeneral);result["state"]=OwnedSolidQuarterTurn.VerificationStateFor(scope);result["copyFenceReleased"]=true;result["nativeConventionQualified"]=true;result["qualificationScope"]=scope;result["recordedJournalState"]=journal.State;result["copyId"]=plan["copyId"];result["ownershipSha256"]=plan["ownershipSha256"];
      }
     }
    }
   }catch(Exception error){result["error"]=error.Message;}finally{if(fenceProofs!=null)fenceProofs.Dispose();}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
