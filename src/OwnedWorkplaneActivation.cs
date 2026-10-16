using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShape.Shared;

namespace PowerShapeBridge {
 // A single context setter per immutable intent. No implicit return or recovery.
 internal static class OwnedWorkplaneActivation {
  internal interface IContext:OwnedWorkplanePlans.IReadContext { void ActivateWorkplane(int composite); }
  internal static readonly string[] Recipes={"prepare_owned_workplane_activation","execute_owned_workplane_activation","verify_owned_workplane_activation"};
  static void Need(bool ok,string why){OwnedWorkplanePlans.Need(ok,why);}
  static Dictionary<string,object> Clone(Dictionary<string,object> v){return OwnedWorkplanePlans.Clone(v);}
  static string Artifact(string root,string folder,object id){return OwnedWorkplanePlans.Artifact(root,folder,id);}
  static Dictionary<string,object> Result(string state){return new Dictionary<string,object>{{"state",state},{"bridgeVersion",Program.ReleaseVersion},{"cadMutationAttempted",false},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"retryAllowed",false},{"nativeProviderQualified",false}};}
  internal static void Parameters(Dictionary<string,object> p){
   Need(p!=null&&p.ContainsKey("mode"),"Explicit activation mode required.");
   if(Equals(p["mode"],"world"))OwnedWorkplanePlans.Fields(p,"mode");
   else {OwnedWorkplanePlans.Fields(p,"mode","nativeId");Need(Equals(p["mode"],"existing")&&p["nativeId"] is int&&(int)p["nativeId"]>=0,"Existing numeric workplane identity required.");}
  }
  internal static void Validate(string recipe,Dictionary<string,object> args){
   Need(Recipes.Contains(recipe),"Unknown workplane activation recipe.");
   if(recipe==Recipes[2]){OwnedTranslation.ValidateVerification(args);return;}
   bool prepare=recipe==Recipes[0];
   if(prepare){OwnedWorkplanePlans.Fields(args,"bindingId","bindingSha256","parameters");Parameters(args["parameters"] as Dictionary<string,object>);}
   else OwnedWorkplanePlans.Fields(args,"activationId","activationSha256");
   foreach(string k in args.Keys.Where(k=>k!="parameters"))Need(EntitySets.Hex(args[k],k.EndsWith("Sha256",StringComparison.Ordinal)?64:32),"Invalid activation proof identity.");
  }
  static double[] Vector(object v){var a=v as IEnumerable;Need(a!=null&&!(v is string),"Numeric vector required.");var r=a.Cast<object>().Select(Program.StrictDouble).ToArray();Need(r.Length==3,"Three finite coordinates required.");return r;}
  static double[][] Axes(double[] f){WorkplaneFrame.Validate(f);var z=f.Skip(3).Take(3).ToArray();var y=f.Skip(6).Take(3).ToArray();var x=new[]{y[1]*z[2]-y[2]*z[1],y[2]*z[0]-y[0]*z[2],y[0]*z[1]-y[1]*z[0]};return new[]{x,y,z};}
  internal static double[] ConvertPoint(double[] p,double[] frame,int source,int target){
   Need(p!=null&&p.Length==3&&p.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)),"Finite COG required.");var axes=Axes(frame);var world=p.ToArray();
   if(source!=0)for(int i=0;i<3;i++)world[i]=frame[i]+Enumerable.Range(0,3).Sum(j=>axes[j][i]*p[j]);
   return target==0?world:axes.Select(a=>Enumerable.Range(0,3).Sum(i=>a[i]*(world[i]-frame[i]))).ToArray();
  }
  static bool Near(double[] a,double[] b){return a.Length==b.Length&&a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-9+1e-12*Math.Max(Math.Abs(x),Math.Abs(y))).All(v=>v);}
  internal static void Empty(Dictionary<string,object> o,int active,IContext c){
   Need(active>=0&&c.ActiveWorkplane()==active&&EntitySets.Canonical(o["contextBefore"])==EntitySets.Canonical(new[]{0,active})&&EntitySets.Canonical(o["contextAfter"])==EntitySets.Canonical(new[]{0,active})&&EntitySets.Canonical(o["selectionBefore"])=="[]"&&EntitySets.Canonical(o["selectionAfter"])=="[]","Stable declared workplane and empty selection required.");
  }
  internal static Dictionary<string,object> Capture(Dictionary<string,object> o,IContext c){
   var entities=EntitySets.Inventory(o);Need(entities.Length==2&&entities.Count(e=>Equals(e["nativeTypeCode"],40))==1&&entities.Count(e=>Equals(e["nativeTypeCode"],54))==1,"First activation provider supports exactly one SOLID and one WORKPLANE; other v1 classes remain unqualified.");
   var frames=OwnedWorkplanePlans.Frames(o,c);var rows=((IEnumerable)frames["frames"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).ToArray();Need(rows.Length==1,"One direct workplane required.");int composite=(int)rows[0]["compositeId"],active=c.ActiveWorkplane();
   Need(active==0||active==composite,"Source workplane is not the observed WORLD or existing workplane.");Empty(o,active,c);
   var body=entities.Single(e=>Equals(e["nativeTypeCode"],40));var geometry=body["geometry"] as Dictionary<string,object>;Need(geometry!=null&&geometry.ContainsKey("cogRaw")&&Equals(geometry["coordinateFrame"],active==0?"world":"active_workplane_unqualified"),"Qualified COG frame label required.");Vector(geometry["cogRaw"]);
   return new Dictionary<string,object>{{"sourceActive",active},{"plane",rows[0]},{"bounds",OwnedWorkplanePlans.Bounds(o,c)}};
  }
  internal static void Postconditions(Dictionary<string,object> plan,Dictionary<string,object> after,IContext c){
   var before=(Dictionary<string,object>)plan["before"];var captured=(Dictionary<string,object>)plan["captureBefore"];var plane=(Dictionary<string,object>)captured["plane"];
   int source=(int)captured["sourceActive"],target=(int)plan["targetCompositeId"];Empty(after,target,c);
   var current=Capture(after,c);Need(EntitySets.Canonical(current["plane"])==EntitySets.Canonical(plane)&&EntitySets.Canonical(current["bounds"])==EntitySets.Canonical(captured["bounds"]),"World workplane frame, numeric mapping or explicit WORLD bounds changed.");
   var oldBody=EntitySets.Inventory(before).Single(e=>Equals(e["nativeTypeCode"],40));var normalized=Clone(after);var body=EntitySets.Inventory(normalized).Single(e=>Equals(e["nativeTypeCode"],40));
   var oldGeometry=(Dictionary<string,object>)oldBody["geometry"];var geometry=(Dictionary<string,object>)body["geometry"];
   var frame=OwnedWorkplanePlans.Info(((IEnumerable)plane["worldInfo"]).Cast<object>().ToArray());var expected=ConvertPoint(Vector(oldGeometry["cogRaw"]),frame,source,target);
   Need(Near(expected,Vector(geometry["cogRaw"])),"COG differs from the mathematically expected active-frame conversion.");
   geometry["cogRaw"]=oldGeometry["cogRaw"];geometry["coordinateFrame"]=oldGeometry["coordinateFrame"];
   normalized["contextBefore"]=before["contextBefore"];normalized["contextAfter"]=before["contextAfter"];
   Need(EntitySets.ObservedDigest(normalized)==EntitySets.ObservedDigest(before),"Observed entities, workplane axes/origin, selection, properties, units or document changed.");
   c.ConfirmDocument();
  }
  static void SavedUnchanged(Dictionary<string,object> plan){
   var prior=plan["savedFileHashesBefore"] as IDictionary;Need(prior!=null,"Saved-file map required.");var current=WorkingCopyOpen.SavedHashes((Dictionary<string,object>)plan["beforeWindows"]);
   Need(current.Count==prior.Count&&current.All(p=>prior.Contains(p.Key)&&Equals(p.Value,prior[p.Key])),"Saved document bytes changed.");
  }
  internal static Dictionary<string,object> Check(string root,string id,Dictionary<string,object> input,bool prepare,IContext c,Action<Dictionary<string,object>,Dictionary<string,object>,OwnedCopies.OwnershipLease,OwnedOperationPlans.Proofs> execute=null){
   var result=Result("owned_workplane_activation_preparation_rejected");result["executionEnabled"]=false;
   try{
    Validate(prepare?Recipes[0]:Recipes[1],input);Need(EntitySets.Hex(id,32),"Invalid request identity.");
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()){
     string packetId=prepare?id:(string)input["activationId"],packetSha=prepare?null:(string)input["activationSha256"];var args=Clone(input);Dictionary<string,object> packet=null;
     if(!prepare){packet=proofs.Json(Artifact(root,"workplaneactivations",packetId),packetSha,65536);Need(Equals(packet["activationId"],packetId)&&Equals(packet["operation"],"activate_existing_workplane_or_world")&&Equals(packet["executionEnabled"],false),"Original activation preparation required.");OwnedOperationPlans.Prepared(root,packetId,packetSha,proofs);args=Clone((Dictionary<string,object>)packet["arguments"]);Validate(Recipes[0],args);}
     var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};var binding=DocumentBindings.Load(root,bindingArgs);
     proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);
     var before=proofs.Json(Artifact(root,"responses",binding["inspectionRequestId"]),(string)binding["inspectionSha256"],16000000);DateTime expiry=EntitySets.Expiry(binding["expiresUtc"]);
     EntitySets.CheckObserved(before,before,new int[0][],expiry,DateTime.UtcNow);
     using(var lease=OwnedCopies.Hold(root,(string)binding["copyId"],(string)binding["ownershipSha256"]))
     using(var bytes=new OwnedWorkingBytePin(lease.WorkingPath)){
      OwnedSaveTargets.NoFence(root,(string)binding["copyId"]);DocumentBindings.Check(lease,before,expiry,c,result,(string)binding["windowContextSha256"]);var capture=Capture(before,c);var plane=(Dictionary<string,object>)capture["plane"];var p=(Dictionary<string,object>)args["parameters"];
      int target=Equals(p["mode"],"world")?0:(int)plane["compositeId"];Need(target!=(int)capture["sourceActive"],"Target already active; no setter is needed.");if(target!=0)Need(Equals(p["nativeId"],plane["nativeId"]),"Requested workplane is absent or belongs to another observation.");
      var computed=new Dictionary<string,object>{{"schemaVersion",1},{"activationId",packetId},{"operation","activate_existing_workplane_or_world"},{"preparedUtc",prepare?DateTime.UtcNow.ToString("o"):packet["preparedUtc"]},{"expiresUtc",expiry.ToString("o")},{"executionEnabled",false},{"arguments",args},{"copyId",binding["copyId"]},{"ownershipSha256",binding["ownershipSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"nativeObservedPropertiesSha256",EntitySets.ObservedDigest(before)},{"windowContextSha256",binding["windowContextSha256"]},{"targetCompositeId",target},{"captureBefore",capture}};
      Need(OwnedCopies.Digest(bytes.Stream)==(string)computed["workingSha256"],"Saved working bytes changed under pin.");
      if(prepare){string path=Artifact(root,"workplaneactivations",packetId);Directory.CreateDirectory(Path.GetDirectoryName(path));packetSha=NativeJournal.Freeze(path,computed);new NativeJournal(root,packetId,packetSha).Append("prepared",new Dictionary<string,object>{{"executionEnabled",false}});packet=proofs.Json(path,packetSha);}
      else Need(EntitySets.Canonical(computed)==EntitySets.Canonical(packet),"Activation preparation dependencies changed.");
      DocumentBindings.Check(lease,before,expiry,c,result,(string)binding["windowContextSha256"]);Empty(c.Observation(),(int)capture["sourceActive"],c);lease.Recheck();OwnedSaveTargets.NoFence(root,(string)binding["copyId"]);
      Need(DateTime.UtcNow<expiry&&OwnedCopies.Digest(bytes.Stream)==(string)computed["workingSha256"],"Final activation lifetime or saved bytes changed.");if(execute!=null)execute(Clone(packet),Clone(before),lease,proofs);
      result["state"]=prepare?"owned_workplane_activation_prepared_observed":"owned_workplane_activation_validated_observed";result["activationId"]=packetId;result["activationSha256"]=packetSha;result["activation"]=Clone(packet);
     }
    }
   }catch(Exception e){result["error"]=e.Message;}result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Execute(string root,string id,Dictionary<string,object> args,IContext c){
   var result=Result("owned_workplane_activation_rejected_before_change");NativeJournal journal=null;bool attempted=false;string fence=null;
   try{
    var guarded=Check(root,id,args,false,c,(packet,before,lease,proofs)=>{
     var windows=c.Windows();Need(DocumentBindings.WindowDigest(windows)==(string)packet["windowContextSha256"],"Bound window context changed.");
     string claim=Path.Combine(root,"workplaneactivations",(string)args["activationId"]+".execution-once.json");Need(!File.Exists(claim)&&!Directory.Exists(claim),"Intent already claimed; replay forbidden.");
     var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"operation","activate_owned_workplane"},{"activationId",args["activationId"]},{"activationSha256",args["activationSha256"]},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"workingSha256",packet["workingSha256"]},{"parameters",((Dictionary<string,object>)packet["arguments"])["parameters"]},{"before",before},{"beforeWindows",windows},{"windowContextSha256",packet["windowContextSha256"]},{"captureBefore",packet["captureBefore"]},{"targetCompositeId",packet["targetCompositeId"]},{"savedFileHashesBefore",WorkingCopyOpen.SavedHashes(windows)},{"createdUtc",DateTime.UtcNow.ToString("o")}};
     string path=Artifact(root,"workplaneactivationexecutions",id);Directory.CreateDirectory(Path.GetDirectoryName(path));string sha=NativeJournal.Freeze(path,plan);result["operationId"]=id;result["plan"]=plan;result["planSha256"]=sha;result["copyId"]=packet["copyId"];
     journal=new NativeJournal(root,id,sha);journal.Append("prepared",new Dictionary<string,object>{{"operation","activate_owned_workplane"}});
     fence=OwnedTranslation.FencePath(root,(string)packet["copyId"]);string fenceSha=NativeJournal.Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"automaticReplayForbidden",true}});result["executionFenceSha256"]=fenceSha;
     string claimSha=NativeJournal.Freeze(claim,new Dictionary<string,object>{{"operationId",id},{"planSha256",sha},{"activationId",args["activationId"]},{"copyId",packet["copyId"]},{"automaticReplayForbidden",true}});result["claimSha256"]=claimSha;proofs.Json(fence,fenceSha);proofs.Json(claim,claimSha);
     journal.Append("execution_claimed",new Dictionary<string,object>{{"nativeRequestId",id},{"activationId",args["activationId"]}});DateTime expiry=EntitySets.Expiry(packet["expiresUtc"]);lease.Recheck();c.ConfirmDocument();
     EntitySets.CheckObserved(before,c.Observation(),new int[0][],expiry,DateTime.UtcNow);Empty(before,(int)((Dictionary<string,object>)packet["captureBefore"])["sourceActive"],c);SavedUnchanged(plan);Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(c.Windows())==(string)plan["windowContextSha256"],"Context changed at command boundary.");
     int target=(int)plan["targetCompositeId"];journal.Append("command_started",new Dictionary<string,object>{{"command","direct COM ActiveWorkplane setter"},{"maximumSetterCalls",1},{"targetCompositeId",target}});c.ConfirmDocument();attempted=true;result["cadMutationAttempted"]=result["contextChangeAttempted"]=true;result["setterCalls"]=1;c.ActivateWorkplane(target);
     Need(c.ActiveWorkplane()==target,"Setter return target not observed; no automatic recovery.");var returned=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",sha},{"targetCompositeId",target},{"activeRaw",c.ActiveWorkplane()},{"setterCalls",1},{"setterReturned",true},{"timestampUtc",DateTime.UtcNow.ToString("o")}};
     string returnSha=NativeJournal.Freeze(Path.Combine(root,"workplaneactivationexecutions",id+".setter-return.json"),returned);result["setterReceiptSha256"]=returnSha;
     journal.Append("command_returned",new Dictionary<string,object>{{"nativeRequestId",id},{"targetCompositeId",target},{"setterCalls",1},{"setterReceiptSha256",returnSha}});
     for(int pass=0;pass<2;pass++){var after=c.Observation();Postconditions(plan,after,c);result["after"]=Clone(after);}lease.Recheck();SavedUnchanged(plan);c.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(c.Windows())==(string)plan["windowContextSha256"],"Post-activation context changed.");
     result["sameWorkerPostconditionsPassed"]=true;result["state"]="owned_workplane_activation_executed_unverified";journal.Append("executed_unverified",new Dictionary<string,object>{{"independentVerificationRequired",true}});
    });Need(Equals(guarded["state"],"owned_workplane_activation_validated_observed"),guarded.ContainsKey("error")?(string)guarded["error"]:"Activation guard rejected.");
   }catch(Exception e){bool unknown=attempted||journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State);result["state"]=unknown?"owned_workplane_activation_outcome_unknown":"owned_workplane_activation_rejected_before_change";result["error"]=e.Message;if(journal!=null&&new[]{"command_started","command_returned"}.Contains(journal.State))try{journal.Append("outcome_unknown",new Dictionary<string,object>{{"cadMutationAttempted",attempted},{"error",e.Message}});}catch(Exception failure){result["journalError"]=failure.Message;}}
   result["recordedJournalState"]=journal==null?null:journal.State;result["copyFenced"]=fence!=null&&(File.Exists(fence)||Directory.Exists(fence));result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Verify(string root,Dictionary<string,object> args,IContext c){
   var result=Result("owned_workplane_activation_verification_rejected");OwnedOperationPlans.Proofs fenceProofs=null;
   try{
    OwnedTranslation.ValidateVerification(args);string id=(string)args["executionRequestId"];
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
    using(var proofs=new OwnedOperationPlans.Proofs()){
     var receipt=proofs.Json(Artifact(root,"responses",id),(string)args["executionReceiptSha256"],16000000);Need(Equals(receipt["operationId"],id)&&new[]{"owned_workplane_activation_executed_unverified","owned_workplane_activation_outcome_unknown"}.Contains((string)receipt["state"])&&Equals(receipt["cadSaveAttempted"],false),"Original activation execution required.");
     var plan=proofs.Json(Artifact(root,"workplaneactivationexecutions",id),(string)receipt["planSha256"],16000000);Need(EntitySets.Canonical(plan)==EntitySets.Canonical(receipt["plan"])&&Equals(plan["operationId"],id)&&Equals(plan["operation"],"activate_owned_workplane"),"Frozen activation execution differs.");
     var packet=proofs.Json(Artifact(root,"workplaneactivations",plan["activationId"]),(string)plan["activationSha256"]);Need(Equals(packet["operation"],"activate_existing_workplane_or_world")&&Equals(packet["targetCompositeId"],plan["targetCompositeId"]),"Original activation intent differs.");
     foreach(string field in new[]{"copyId","ownershipSha256","workingSha256","windowContextSha256","captureBefore"})Need(EntitySets.Canonical(packet[field])==EntitySets.Canonical(plan[field]),"Frozen activation dependency differs: "+field);
     var originalArgs=(Dictionary<string,object>)packet["arguments"];Need(EntitySets.Canonical(originalArgs["parameters"])==EntitySets.Canonical(plan["parameters"]),"Original activation parameters differ.");var originalBinding=proofs.Json(Artifact(root,"documentbindings",originalArgs["bindingId"]),(string)originalArgs["bindingSha256"]);var originalBefore=proofs.Json(Artifact(root,"responses",originalBinding["inspectionRequestId"]),(string)originalBinding["inspectionSha256"],16000000);Need(EntitySets.Canonical(originalBefore)==EntitySets.Canonical(plan["before"]),"Original observed baseline differs.");
     var journal=NativeJournal.ForVerification(root,id,(string)receipt["planSha256"]);var returned=journal.CommandReturnedDetails;OwnedWorkplanePlans.Fields(returned,"nativeRequestId","targetCompositeId","setterCalls","setterReceiptSha256");Need(Equals(returned["nativeRequestId"],id)&&Equals(returned["targetCompositeId"],plan["targetCompositeId"])&&Equals(returned["setterCalls"],1),"Authoritative setter return required.");
     var setter=proofs.Json(Path.Combine(root,"workplaneactivationexecutions",id+".setter-return.json"),(string)returned["setterReceiptSha256"]);OwnedWorkplanePlans.Fields(setter,"schemaVersion","operationId","planSha256","targetCompositeId","activeRaw","setterCalls","setterReturned","timestampUtc");Need(Equals(setter["schemaVersion"],1)&&Equals(setter["operationId"],id)&&Equals(setter["planSha256"],receipt["planSha256"])&&Equals(setter["targetCompositeId"],plan["targetCompositeId"])&&Equals(setter["activeRaw"],plan["targetCompositeId"])&&Equals(setter["setterCalls"],1)&&Equals(setter["setterReturned"],true),"Setter return proof differs.");
     var claim=proofs.Json(Path.Combine(root,"workplaneactivations",(string)plan["activationId"]+".execution-once.json"),(string)receipt["claimSha256"]);OwnedWorkplanePlans.Fields(claim,"operationId","planSha256","activationId","copyId","automaticReplayForbidden");Need(Equals(claim["operationId"],id)&&Equals(claim["planSha256"],receipt["planSha256"])&&Equals(claim["activationId"],plan["activationId"])&&Equals(claim["copyId"],plan["copyId"])&&Equals(claim["automaticReplayForbidden"],true),"Activation once claim differs.");
     var observed=proofs.Json(Artifact(root,"responses",args["inspectionRequestId"]),(string)args["inspectionSha256"],16000000);var listing=proofs.Json(Artifact(root,"responses",args["listingRequestId"]),(string)args["listingSha256"],16000000);DateTime expiry=EntitySets.Expiry(args["expiresUtc"]);EntitySets.CheckObserved(observed,observed,new int[0][],expiry,DateTime.UtcNow);
     Need(Equals(listing["state"],"documents_listed_verified")&&DocumentBindings.WindowDigest(listing)==(string)plan["windowContextSha256"],"Independent listing differs.");var active=(Dictionary<string,object>)listing["activeWindow"];Need(Equals(active["windowNumber"],args["windowNumber"])&&Equals(active["modelId"],args["modelId"]),"Independent choice is not active owned document.");
     foreach(var read in new[]{observed,listing}){var stamp=EntitySets.Expiry(read["timestampUtc"]);Need(stamp>journal.CommandReturnedUtc&&stamp<=DateTime.UtcNow.AddSeconds(5)&&expiry>stamp&&expiry<=stamp.AddSeconds(300),"Independent read is stale or precedes setter return.");}
     using(var lease=OwnedCopies.Hold(root,(string)plan["copyId"],(string)plan["ownershipSha256"])){
      OwnedGeometryExportTargets.NoExportFence(root,(string)plan["copyId"]);Need(Equals(lease.Inspection["workingSha256"],plan["workingSha256"]),"Saved working bytes changed.");string fence=OwnedTranslation.FencePath(root,(string)plan["copyId"]);bool exists=File.Exists(fence);
      if(exists){fenceProofs=new OwnedOperationPlans.Proofs();var f=fenceProofs.Json(fence,(string)receipt["executionFenceSha256"]);OwnedWorkplanePlans.Fields(f,"schemaVersion","operationId","planSha256","copyId","ownershipSha256","automaticReplayForbidden");Need(Equals(f["schemaVersion"],1)&&Equals(f["operationId"],id)&&Equals(f["planSha256"],receipt["planSha256"])&&Equals(f["copyId"],plan["copyId"])&&Equals(f["ownershipSha256"],plan["ownershipSha256"])&&Equals(f["automaticReplayForbidden"],true),"Foreign activation fence retained.");}
      else Need(journal.State=="succeeded_verified"&&!Directory.Exists(fence),"Unverified activation fence is missing.");
      DocumentBindings.Check(lease,observed,expiry,c,result,(string)plan["windowContextSha256"]);for(int pass=0;pass<2;pass++){var fresh=c.Observation();EntitySets.CheckObserved(observed,fresh,new int[0][],expiry,DateTime.UtcNow);Postconditions(plan,fresh,c);}SavedUnchanged(plan);lease.Recheck();c.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(c.Windows())==(string)plan["windowContextSha256"],"Independent final context changed.");
      var evidence=new[]{new Dictionary<string,object>{{"requestId",id},{"sha256",args["executionReceiptSha256"]}},new Dictionary<string,object>{{"requestId",args["inspectionRequestId"]},{"sha256",args["inspectionSha256"]}},new Dictionary<string,object>{{"requestId",args["listingRequestId"]},{"sha256",args["listingSha256"]}}};
      if(journal.State!="succeeded_verified")journal.Append("succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"targetCompositeId",plan["targetCompositeId"]},{"verificationScope","one existing workplane context setter; observed metadata/world bounds/frame and expected COG basis conversion; not full topology or persisted active context"}});
      if(exists){fenceProofs.Dispose();fenceProofs=null;NativeJournal.VerifiedJson(fence,(string)receipt["executionFenceSha256"],65536);File.Delete(fence);}result["state"]="owned_workplane_activation_verified_observed";result["executionOperationId"]=id;result["copyFenceReleased"]=true;result["recordedJournalState"]=journal.State;result["noCommandReplay"]=true;result["evidence"]=evidence;result["activeWorkplaneAfter"]=plan["targetCompositeId"];
     }
    }
   }catch(Exception e){result["error"]=e.Message;}finally{if(fenceProofs!=null)fenceProofs.Dispose();}result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  internal static Dictionary<string,object> Run(string id,string recipe,Dictionary<string,object> args){
   var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.ownedWorkplaneActivationExperimental,"Owned workplane activation disabled.");Validate(recipe,args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var c=new DocumentBindings.ComContext(config)){if(recipe==Recipes[0])return Check(Program.Root,id,args,true,c);if(recipe==Recipes[1])return Execute(Program.Root,id,args,c);return Verify(Program.Root,args,c);}
  }
 }
}
