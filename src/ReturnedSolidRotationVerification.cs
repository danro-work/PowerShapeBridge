using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;
using IContext = PowerShapeBridge.OwnedRotationVerification.IContext;

namespace PowerShapeBridge {
 // Independent read context only. Generic verification retains fences; the native-backed
 // one-SOLID WORLD normalized-axis quarter-turn entry point can release only its verified matching fence.
 internal static class ReturnedSolidRotationVerification {
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static void Fields(Dictionary<string,object> value,params string[] fields){Require(value.Count==fields.Length&&fields.All(value.ContainsKey),"Verification proof fields differ.");}
  static string Artifact(string root,string folder,string id){Require(EntitySets.Hex(id,32),"Fixed proof identity required.");string path=Path.Combine(root,folder,id+".json");OwnedCopies.NoReparse(path);return path;}
  static object[] Values(object value){var list=value as IList;Require(list!=null,"Explicit proof array required.");return list.Cast<object>().ToArray();}
  static double[] Numbers(object value){return Values(value).Select(v=>{Require(v is int||v is long||v is double||v is float||v is decimal,"Native numeric evidence required.");double number=Program.StrictDouble(v);Require(!double.IsNaN(number)&&!double.IsInfinity(number),"Finite numeric evidence required.");return number;}).ToArray();}
  internal static void Validate(Dictionary<string,object> args) {
   string[] ids={"executionRequestId","inspectionRequestId","listingRequestId"},shas={"executionReceiptSha256","inspectionSha256","listingSha256"};
   Require(args.Count==9&&ids.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],32))&&shas.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],64))&&args.ContainsKey("windowNumber")&&args["windowNumber"] is int&&args.ContainsKey("modelId")&&args["modelId"] is int&&args.ContainsKey("expiresUtc"),"Exact independent rotation references required.");EntitySets.Expiry(args["expiresUtc"]);
  }
  internal static Dictionary<string,object> Verify(string root,string verificationId,Dictionary<string,object> args,SolidRotationContext.ReadContext context){return VerifyCore(root,verificationId,args,context,2);}
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
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()) {
     string id=(string)args["executionRequestId"];var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["executionReceiptSha256"],16000000);
     Require(qualifyGeneral&&context is SolidRotationContext.ReadContext&&((SolidRotationContext.ReadContext)context).IsNativeProvider&&Equals(receipt["operationId"],id)&&Equals(receipt["state"],"owned_rotation_outcome_unknown")&&Equals(receipt["commandCallsAttempted"],1)&&Equals(receipt["geometryMutationAttempted"],true)&&Equals(receipt["contextChangeAttempted"],false)&&Equals(receipt["cadSaveAttempted"],false),"Only native single returned SOLID reconciliation supported.");
     var plan=proofs.Json(Artifact(root,"rotationexecutions",id),(string)receipt["planSha256"],16000000);Require(Equals(plan["schemaVersion"],2)&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"rotate_owned_entities")&&EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"]),"Pinned measured execution plan differs.");
     var targets=Values(plan["targets"]).Cast<Dictionary<string,object>>().ToArray();Require(targets.Length==1,"Bounded targets required.");var keys=targets.Select(target=>EntitySets.Keys(new[]{target["entityKey"]}).Single()).ToArray();Require(keys.Select(k=>k[0]+":"+k[1]).Distinct().Count()==keys.Length&&targets.Select(t=>t["compositeId"]).Distinct().Count()==targets.Length,"Duplicate execution targets.");
     var originalJournal=ReturnedRotationEvidence.Journal(root,id,(string)receipt["planSha256"],proofs);var commandEvidence=ReturnedRotationEvidence.Command(root,id,(string)receipt["planSha256"],plan,proofs);DateTime returnedUtc=EntitySets.Expiry(commandEvidence["returnedUtc"]);Require(returnedUtc>=EntitySets.Expiry(originalJournal["startedUtc"])&&returnedUtc<=EntitySets.Expiry(originalJournal["unknownUtc"]),"Native return chronology differs.");var commands=new[]{commandEvidence};
     var before=proofs.Json(Artifact(root,"responses",(string)plan["inspectionRequestId"]),(string)plan["inspectionSha256"],16000000);var expected=proofs.Json(Artifact(root,"responses",(string)args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Artifact(root,"responses",(string)args["listingRequestId"]),(string)args["listingSha256"],16000000);var expiry=EntitySets.Expiry(args["expiresUtc"]);
     EntitySets.CheckObserved(expected,expected,keys,expiry,DateTime.UtcNow);Require(EntitySets.Expiry(expected["timestampUtc"])>returnedUtc&&EntitySets.Expiry(listing["timestampUtc"])>returnedUtc&&expiry<=EntitySets.Expiry(listing["timestampUtc"]).AddSeconds(300)&&Equals(listing["state"],"documents_listed_verified"),"Independent reads must follow aggregate command return.");
     var active=(Dictionary<string,object>)listing["activeWindow"];Require(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"])&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Fresh active listing differs.");OwnedRotationExecution.PreserveOthers(before,expected,keys);
     var provenance=(Dictionary<string,object>)plan["measurementProvenance"];Require(Equals(provenance["copyId"],plan["copyId"])&&Equals(provenance["ownershipSha256"],plan["ownershipSha256"])&&Equals(provenance["workingSha256"],plan["workingSha256"]),"Measurement owner differs.");
     using(var lease=OwnedCopies.Hold(root,(string)plan["copyId"],(string)plan["ownershipSha256"])) {
      Require(Equals(lease.Inspection["workingSha256"],plan["workingSha256"]),"Saved working bytes changed.");string fencePath=OwnedTranslation.FencePath(root,(string)plan["copyId"]);fenceProofs=new OwnedOperationPlans.Proofs();var fence=fenceProofs.Json(fencePath,(string)receipt["executionFenceSha256"]);Require(Equals(fence["operationId"],id)&&Equals(fence["planSha256"],receipt["planSha256"])&&Equals(fence["copyId"],plan["copyId"])&&Equals(fence["ownershipSha256"],plan["ownershipSha256"])&&Equals(fence["automaticReplayForbidden"],true),"Original fence differs.");
      var claim=proofs.Json(Path.Combine(root,"rotationplans",(string)plan["rotationPlanId"]+".execution-once.json"),null);Require(Equals(claim["operationId"],id)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["copyId"],plan["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Original one-use claim differs.");
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);
      var measuredTargets=new List<Dictionary<string,object>>();
      for(int index=0;index<targets.Length;index++) {
       var command=commands[index];Require(Equals(command["index"],index)&&EntitySets.Canonical(command["entityKey"])==EntitySets.Canonical(targets[index]["entityKey"])&&Equals(command["beforeMeasurementSha256"],targets[index]["beforeMeasurementSha256"]),"Command target/order differs.");
       var original=RotationMeasurementRecords.Read(root,id,index,"before",(string)command["beforeMeasurementSha256"],provenance,proofs);Require(original.Type==40&&original.NativeId==keys[index][1],"Pinned before measurement differs.");var originalRecord=proofs.Json(RotationMeasurementRecords.PathFor(root,id,index,"before"),(string)command["beforeMeasurementSha256"],16000000);Require(EntitySets.Expiry(originalRecord["recordedUtc"])<=EntitySets.Expiry(plan["createdUtc"]),"Before measurement chronology differs.");
       int composite=context.Composite(keys[index][0],keys[index][1]);Require(Equals(composite,targets[index]["compositeId"]),"Fresh mapping changed.");var first=context.Measurements(composite);var second=context.Measurements(composite);Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(first))==EntitySets.Canonical(RotationMeasurementRecords.Encode(second)),"Fresh independent samples changed during reads.");
       var recordedAfter=first;Require(context.Bounds(composite).SequenceEqual(second.BoundsPaired),"Independent/direct bounds differ.");
       if((qualifyQuarterTurn||qualifyGeneral)&&NativeRotationScope(context,plan,keys,expected,qualifyGeneral)&&(qualifyGeneral||OwnedSolidQuarterTurn.ScopeFor(Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]))==OwnedSolidQuarterTurn.ObliqueScope)){
        OwnedSolidQuarterTurn.VerifySolidSamples(original,recordedAfter,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),qualifyGeneral);OwnedSolidQuarterTurn.VerifySolidSamples(original,second,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),qualifyGeneral);result["numericVerificationProfile"]=OwnedSolidQuarterTurn.NumericProfileForMode(qualifyGeneral);
       }else {RotationSampleVerifier.Verify(original,recordedAfter,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),1e-6,1e-9);RotationSampleVerifier.Verify(original,second,Program.StrictInt(plan["angleDegrees"]),Numbers(plan["nativeAxis"]),1e-6,1e-9);}
       Require(EntitySets.Canonical(RotationMeasurementRecords.Encode(recordedAfter))==EntitySets.Canonical(RotationMeasurementRecords.Encode(second)),"Recorded and independent samples differ.");
       measuredTargets.Add(new Dictionary<string,object>{{"index",index},{"entityKey",keys[index]},{"compositeId",composite},{"beforeMeasurementSha256",command["beforeMeasurementSha256"]},{"nativeCallbackReturnedSha256",command["returnedSha256"]},{"freshSamples",RotationMeasurementRecords.Encode(second)}});
      }
      DocumentBindings.Check(lease,expected,expiry,context,result,(string)plan["windowContextSha256"]);var saved=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["beforeWindows"]);var originalSaved=(Dictionary<string,object>)plan["savedFileHashesBefore"];Require(saved.Count==originalSaved.Count&&saved.All(pair=>originalSaved.ContainsKey(pair.Key)&&Equals(originalSaved[pair.Key],pair.Value)),"Saved documents changed.");lease.Recheck();context.ConfirmDocument();
      Require(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(context.Windows())==(string)plan["windowContextSha256"],"Verification context/expiry changed before publication.");
      var diagnostic=new Dictionary<string,object>{{"schemaVersion",1},{"verificationId",verificationId},{"executionRequestId",id},{"executionReceiptSha256",args["executionReceiptSha256"]},{"planSha256",receipt["planSha256"]},{"inspectionRequestId",args["inspectionRequestId"]},{"inspectionSha256",args["inspectionSha256"]},{"listingRequestId",args["listingRequestId"]},{"listingSha256",args["listingSha256"]},{"targetCount",targets.Length},{"measuredTargets",measuredTargets},{"state","observed_returned_single_rotation_samples_match"},{"nativeConventionQualified",false},{"copyFenceReleased",false},{"qualification","Observed anchored samples/invariants/bounds; no full topology or qualified native convention"}};
      diagnostic["reconciliationRoute"]="single_return_record";diagnostic["originalJournalModified"]=false;diagnostic["originalJournal"] = originalJournal;diagnostic["commandEvidence"]=commandEvidence;
      if(result.ContainsKey("numericVerificationProfile"))diagnostic["numericVerificationProfile"]=result["numericVerificationProfile"];
      string path=Artifact(root,"rotationverifications",verificationId);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,diagnostic);result["state"]=diagnostic["state"];result["diagnosticSha256"]=sha;result["targetCount"]=targets.Length;result["samplePostconditionsPassed"]=true;result["executionRequestId"]=id;result["recordedJournalState"]="outcome_unknown";
      if(qualifyQuarterTurn||qualifyGeneral) {
       Require(NativeRotationScope(context,plan,keys,expected,qualifyGeneral),"Outside the one-SOLID signed WORLD normalized-axis quarter-turn/mm scope.");
       Require(receipt.ContainsKey("solidWitnessBundleSha256")&&EntitySets.Hex(receipt["solidWitnessBundleSha256"],64),"Pinned native solid witness bundle required.");
       var bundle=proofs.Json(Artifact(root,"solidrotationdescriptors",(string)plan["ownedRotationId"]),(string)receipt["solidWitnessBundleSha256"],16000000);
       foreach(string key in new[]{"ownedRotationId","ownedRotationSha256","bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})Require(Equals(bundle[key],plan[key]),"Qualified witness bundle differs from original execution: "+key);
       Require(EntitySets.Canonical(bundle["descriptor"])==((SolidRotationContext.ReadContext)context).PinnedDescriptor,"Qualified reader descriptor differs.");
       var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
       Require(EntitySets.Canonical(ReturnedRotationEvidence.Journal(root,id,(string)receipt["planSha256"],proofs))==EntitySets.Canonical(originalJournal),"Original journal changed during reconciliation.");result["reconciliationRoute"]="single_return_record";result["originalJournalModified"]=false;result["noCommandReplay"]=true;
       fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fencePath,(string)receipt["executionFenceSha256"],65536);File.Delete(fencePath);
       string scope=OwnedSolidQuarterTurn.ScopeForMode((int)plan["angleDegrees"],Numbers(plan["nativeAxis"]),qualifyGeneral);result["state"]=OwnedSolidQuarterTurn.VerificationStateFor(scope);result["copyFenceReleased"]=true;result["nativeConventionQualified"]=true;result["qualificationScope"]=scope;result["recordedJournalState"]="outcome_unknown";result["copyId"]=plan["copyId"];result["ownershipSha256"]=plan["ownershipSha256"];
      }
     }
    }
   }catch(Exception error){result["error"]=error.Message;}finally{if(fenceProofs!=null)fenceProofs.Dispose();}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
