using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class ReturnedRotationEvidence {
  internal static void Need(bool yes,string why){if(!yes)throw new InvalidDataException(why);}
  internal static Dictionary<string,object> Journal(string root,string id,string planSha,OwnedOperationPlans.Proofs proofs){
   string folder=Path.Combine(root,"operations",id);OwnedCopies.NoReparse(folder);Need(!File.Exists(Path.Combine(folder,"writer.lock")),"Interrupted journal writer must be inspected.");var names=Directory.GetFiles(folder,"*.json").OrderBy(v=>v,StringComparer.Ordinal).ToArray();Need(names.Length==4,"Exactly four original unknown journal events required.");
   var hashes=new List<Dictionary<string,object>>();string previous=null;DateTime started=DateTime.MinValue,unknown=DateTime.MinValue;string prior=null;string[] states={"prepared","execution_claimed","command_started","outcome_unknown"};
   for(int i=0;i<4;i++){Need(Path.GetFileName(names[i])==i.ToString("D6")+".json","Journal sequence differs.");var e=proofs.Json(names[i],null);Need(e.Count==8&&Equals(e["schemaVersion"],1)&&Equals(e["operationId"],id)&&Equals(e["planSha256"],planSha)&&Equals(e["sequence"],i)&&Equals(e["previousSha256"],previous)&&Equals(e["state"],states[i]),"Original unknown journal chain differs.");var details=(Dictionary<string,object>)e["details"];NativeJournal.Transition(prior,states[i],details);if(i==1)Need(Equals(details["nativeRequestId"],id)&&Equals(details["targetCount"],1),"One claimed native request required.");if(i==2){Need(Equals(details["targetCount"],1),"One started target required.");started=EntitySets.Expiry(e["timestampUtc"]);}if(i==3){Need(Equals(details["commandCallsAttempted"],1),"Exactly one attempted callback required.");unknown=EntitySets.Expiry(e["timestampUtc"]);}prior=states[i];previous=Program.Hash(names[i]);hashes.Add(new Dictionary<string,object>{{"sequence",i},{"sha256",previous}});}
   return new Dictionary<string,object>{{"hashes",hashes},{"startedUtc",started.ToString("o")},{"unknownUtc",unknown.ToString("o")}};
  }
  internal static Dictionary<string,object> Command(string root,string id,string planSha,Dictionary<string,object> plan,OwnedOperationPlans.Proofs proofs){
   var targets=((System.Collections.IEnumerable)plan["targets"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).ToArray();Need(targets.Length==1&&EntitySets.Keys(new[]{targets[0]["entityKey"]})[0][0]==40,"Only one SOLID target may be reconciled.");string prefix=Path.Combine(root,"rotationcommands",id,"000000");var files=Directory.GetFiles(Path.GetDirectoryName(prefix),"*.json");Need(files.Length==2&&!File.Exists(prefix+".observed.json")&&!File.Exists(Path.Combine(Path.GetDirectoryName(prefix),"summary.json")),"Not the isolated single-return gap.");
   var started=proofs.Json(prefix+".started.json",null);string startedSha=Program.Hash(prefix+".started.json");var returned=proofs.Json(prefix+".returned.json",null);string returnedSha=Program.Hash(prefix+".returned.json");
   Need(started.Count==8&&Equals(started["operationId"],id)&&Equals(started["planSha256"],planSha)&&Equals(started["index"],0)&&EntitySets.Canonical(started["entityKey"])==EntitySets.Canonical(targets[0]["entityKey"])&&Equals(started["compositeId"],targets[0]["compositeId"])&&Equals(started["angleDegrees"],plan["angleDegrees"])&&EntitySets.Canonical(started["nativeAxis"])==EntitySets.Canonical(plan["nativeAxis"]),"Original started command differs.");
   Need(returned.Count==6&&Equals(returned["operationId"],id)&&Equals(returned["planSha256"],planSha)&&Equals(returned["index"],0)&&Equals(returned["startedSha256"],startedSha)&&Equals(returned["returnCode"],1),"Authoritative single callback return1 required; never infer return.");Need(!File.Exists(RotationMeasurementRecords.PathFor(root,id,0,"after")),"Existing after evidence requires normal verification.");
   return new Dictionary<string,object>{{"index",0},{"entityKey",targets[0]["entityKey"]},{"beforeMeasurementSha256",targets[0]["beforeMeasurementSha256"]},{"startedSha256",startedSha},{"returnedSha256",returnedSha},{"returnCode",1},{"returnedUtc",returned["timestampUtc"]}};
  }
 }
}
