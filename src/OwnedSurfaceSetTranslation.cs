using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 // Typed strategy for the shared OwnedTranslation lifecycle, not another executor.
 internal static class OwnedSurfaceSetTranslation {
  internal const string Profile="owned_surface_set_grid_translation_v1";
  internal const string Operation="translate_owned_surface_set";
  internal const string Qualification="Experimental explicitly ordered owned SURFACE set WORLD translation in mm. Sequential TranslateItem calls, not atomic; per-step records and full target/non-target UV witnesses. Unknown stops the set and retains its fence. No selection/save/inverse/replay; sampled geometry only, not full topology.";
  static void Need(bool v,string why){if(!v)throw new InvalidDataException(why);}
  static Dictionary<string,object> Clone(object v){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));}
  internal static int[][] Targets(OwnedTransformSource d){
   Need(Equals(d.Plan["operation"],"translate_entities")&&d.Keys.Length>=2&&d.Keys.Length<=8&&d.Keys.All(k=>k.Length==2&&k[0]==21&&k[1]>0)&&d.Keys.Select(k=>k[1]).Distinct().Count()==d.Keys.Length,"Two to eight explicit distinct SURFACE targets required.");
   Need(Equals(d.Expected["lengthUnitsRaw"],"mm")&&OwnedSurfaceTranslation.Number(d.Expected["lengthUnitFactorRaw"])==1,"Resolved WORLD raw mm required.");
   var inventory=EntitySets.Inventory(d.Expected);Need(inventory.Length>=d.Keys.Length&&inventory.Length<=8&&inventory.All(e=>Equals(e["nativeTypeCode"],21))&&d.Keys.All(k=>inventory.Count(e=>Equals(e["nativeId"],k[1]))==1),"Exact target membership in a surface-only model required.");
   return d.Keys.Select(k=>k.ToArray()).ToArray();
  }
  internal static int[][] PlanTargets(Dictionary<string,object> plan){var keys=EntitySets.Keys(plan["targetEntityKeys"]);Need(keys.Length>=2&&keys.Length<=8&&keys.All(k=>k[0]==21&&k[1]>0),"Surface set execution targets differ.");return keys;}
  internal static int[] Composites(Dictionary<string,object> plan,int count){var list=plan["targetCompositeIds"]as IList;Need(list!=null&&list.Count==count&&list.Cast<object>().All(v=>v is int&&(int)v>0),"Surface set composites differ.");var a=list.Cast<int>().ToArray();Need(a.Distinct().Count()==count,"Surface set composite aliases rejected.");return a;}
  internal static Dictionary<string,object> Finish(Dictionary<string,object> r,bool enabled,bool numerical=false){if(enabled){string state=(string)r["state"];if(state.StartsWith("owned_translation_",StringComparison.Ordinal))r["state"]="owned_surface_set_translation_"+state.Substring("owned_translation_".Length);r["qualification"]=numerical?OwnedSurfaceSetNumerical.Qualification:Qualification;r["qualificationScope"]=numerical?OwnedSurfaceSetNumerical.Profile:Profile;if(numerical)r["normalPolicy"]=OwnedSurfaceSetNumerical.Policy();}return r;}
  internal static int PerformSteps(string root,string id,string planSha,Dictionary<string,object> plan,Dictionary<string,object> before,DateTime expiry,OwnedTranslation.IContext c,Dictionary<string,object> result,Action recheck,Action attempted,bool numerical=false){
   var keys=PlanTargets(plan);var composites=Composites(plan,keys.Length);var original=(Dictionary<string,object>)plan["captureBefore"];var grid=(Dictionary<string,object>)plan["surfaceGrid"];var vector=OwnedSurfaceTranslation.Vector(plan["vectorRaw"]);int anchor=keys[0][1];var steps=new Steps(root,id,planSha,keys,composites,numerical?OwnedSurfaceSetNumerical.Profile:Profile);
   try{for(int i=0;i<keys.Length;i++){
    recheck();var observed=c.Observation();var current=OwnedSurfaceTranslation.Capture(observed,expiry,c,anchor,grid);if(i==0)Need(OwnedSurfaceSelection.Same(original,current),"Initial surface set witnesses changed.");else if(numerical)OwnedSurfaceSetNumerical.Post(before,observed,original,current,vector,keys.Take(i).ToArray());else OwnedSurfaceTranslation.PostTargets(before,observed,original,current,vector,keys.Take(i).ToArray());
    for(int j=0;j<keys.Length;j++)Need(c.Composite(21,keys[j][1])==composites[j],"A set target composite changed before callback.");
    recheck();steps.Started();c.ConfirmDocument();Need(DateTime.UtcNow<expiry&&DocumentBindings.WindowDigest(c.Windows())==(string)plan["windowContextSha256"],"Set step context/expiry changed before callback.");attempted();int code=c.Translate(composites[i],vector);steps.Returned(code);Need(code==1,"Set TranslateItem returned non-success; stop without replay.");
    observed=c.Observation();current=OwnedSurfaceTranslation.Capture(observed,expiry,c,anchor,grid);if(numerical)OwnedSurfaceSetNumerical.Post(before,observed,original,current,vector,keys.Take(i+1).ToArray());else OwnedSurfaceTranslation.PostTargets(before,observed,original,current,vector,keys.Take(i+1).ToArray());recheck();steps.Observed(observed,current);
   }return 1;}finally{result["stepProof"]=steps.Proof();}
  }
  internal sealed class Steps {
   readonly string folder,id,planSha,profile;readonly int[][] keys;readonly int[] composites;readonly List<Dictionary<string,object>> rows=new List<Dictionary<string,object>>();int next;string phase="ready",previous;
   internal Steps(string root,string operationId,string sha,int[][] targets,int[] nativeComposites,string selectedProfile=Profile){profile=selectedProfile;Need(EntitySets.Hex(operationId,32)&&EntitySets.Hex(sha,64)&&targets.Length==nativeComposites.Length,"Exact step operation identity required.");id=operationId;planSha=sha;keys=targets.Select(k=>k.ToArray()).ToArray();composites=nativeComposites.ToArray();folder=Path.Combine(root,"surfacesetsteps",id);OwnedCopies.NoReparse(folder);Need(!Directory.Exists(folder)&&!File.Exists(folder),"Step history exists; no replay.");Directory.CreateDirectory(folder);}
   string Freeze(string kind,Dictionary<string,object> details){var value=new Dictionary<string,object>{{"schemaVersion",1},{"recordType",kind},{"operationId",id},{"planSha256",planSha},{"index",next},{"entityKey",keys[next]},{"compositeId",composites[next]},{"previousSha256",previous},{"timestampUtc",DateTime.UtcNow.ToString("o")}};foreach(var p in details)value.Add(p.Key,p.Value);string sha=NativeJournal.Freeze(Path.Combine(folder,next.ToString("D4")+"."+kind+".json"),value);previous=sha;return sha;}
   internal void Started(){Need(phase=="ready"&&next<keys.Length,"Unexpected/repeated step start.");string sha=Freeze("started",new Dictionary<string,object>());rows.Add(new Dictionary<string,object>{{"index",next},{"startedSha256",sha},{"returnedSha256",null},{"observedSha256",null}});phase="started";}
   internal void Returned(int code){Need(phase=="started","Native return without matching start.");rows[next]["returnedSha256"]=Freeze("returned",new Dictionary<string,object>{{"returnCode",code}});phase=code==1?"returned":"stopped";}
   internal void Observed(Dictionary<string,object> observation,Dictionary<string,object> capture){Need(phase=="returned","No successful native return for observed step.");rows[next]["observedSha256"]=Freeze("observed",new Dictionary<string,object>{{"observation",observation},{"capture",capture}});next++;phase="ready";}
   internal Dictionary<string,object> Proof(){return Clone(new Dictionary<string,object>{{"schemaVersion",1},{"profile",profile},{"operationId",id},{"planSha256",planSha},{"targetCount",keys.Length},{"completedSteps",next},{"phase",phase},{"lastSha256",previous},{"records",rows.ToArray()}});}
  }
  internal static string VerifySteps(string root,string id,string planSha,Dictionary<string,object> receipt,Dictionary<string,object> plan,Dictionary<string,object> before,OwnedOperationPlans.Proofs held,DateTime returnedUtc,bool numerical=false){
   var keys=PlanTargets(plan);var composites=Composites(plan,keys.Length);var sourcePlan=held.Json(Path.Combine(root,"operationplans",(string)plan["operationPlanId"]+".json"),(string)plan["operationPlanSha256"]);var sourceSets=((IEnumerable)sourcePlan["entitySets"]).Cast<Dictionary<string,object>>().ToArray();Need(Equals(sourcePlan["operation"],"translate_entities")&&sourceSets.Length==1&&OwnedSurfaceSelection.Same(sourceSets[0]["entityKeys"],keys),"Set targets differ from original immutable operation plan.");
   var proof=receipt["stepProof"]as Dictionary<string,object>;Need(proof!=null&&proof.Count==9&&Equals(proof["schemaVersion"],1)&&Equals(proof["profile"],numerical?OwnedSurfaceSetNumerical.Profile:Profile)&&Equals(proof["operationId"],id)&&Equals(proof["planSha256"],planSha)&&Equals(proof["targetCount"],keys.Length)&&Equals(proof["completedSteps"],keys.Length)&&Equals(proof["phase"],"ready"),"Complete authoritative set step proof required.");
   var rows=((IEnumerable)proof["records"]).Cast<Dictionary<string,object>>().ToArray();Need(rows.Length==keys.Length,"Exact set step record count required.");string folder=Path.Combine(root,"surfacesetsteps",id);OwnedCopies.NoReparse(folder);Need(Directory.GetFiles(folder).Length==3*keys.Length&&Directory.GetDirectories(folder).Length==0,"Extra/missing set step artifacts.");string previous=null;DateTime prior=EntitySets.Expiry(plan["createdUtc"]);
   for(int i=0;i<keys.Length;i++){
    Need(rows[i].Count==4&&Equals(rows[i]["index"],i),"Ordered set step proof differs.");
    foreach(string kind in new[]{"started","returned","observed"}){
     var value=held.Json(Path.Combine(folder,i.ToString("D4")+"."+kind+".json"),(string)rows[i][kind+"Sha256"],16000000);Need(value.Count==(kind=="started"?9:kind=="returned"?10:11)&&Equals(value["schemaVersion"],1)&&Equals(value["recordType"],kind)&&Equals(value["operationId"],id)&&Equals(value["planSha256"],planSha)&&Equals(value["index"],i)&&OwnedSurfaceSelection.Same(value["entityKey"],keys[i])&&Equals(value["compositeId"],composites[i])&&Equals(value["previousSha256"],previous),"Set step identity/hash chain differs.");DateTime utc=EntitySets.Expiry(value["timestampUtc"]);Need(utc>=prior&&utc<=returnedUtc,"Set step chronology differs.");prior=utc;previous=(string)rows[i][kind+"Sha256"];
     if(kind=="returned")Need(Equals(value["returnCode"],1),"Every set callback must authoritatively return1.");
     if(kind=="observed"){if(numerical)OwnedSurfaceSetNumerical.Post(before,(Dictionary<string,object>)value["observation"],(Dictionary<string,object>)plan["captureBefore"],(Dictionary<string,object>)value["capture"],OwnedSurfaceTranslation.Vector(plan["vectorRaw"]),keys.Take(i+1).ToArray());else OwnedSurfaceTranslation.PostTargets(before,(Dictionary<string,object>)value["observation"],(Dictionary<string,object>)plan["captureBefore"],(Dictionary<string,object>)value["capture"],OwnedSurfaceTranslation.Vector(plan["vectorRaw"]),keys.Take(i+1).ToArray());}
    }
   }
   Need(Equals(proof["lastSha256"],previous),"Final step chain hash differs.");return previous;
  }
 }
}
