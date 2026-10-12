using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerShapeBridge {
 // Coupled passive guard. This version has no command callback or geometry executor.
 public static class OwnedOperationPlans {
  public const string Qualification="Observed coupled plan/set/owned-document guard only. Artifact and ownership leases plus one retained COM application/document during this call. Execution disabled; standalone result never authorizes a later edit, persistent identity or full topology.";
  internal sealed class Proofs : IDisposable {
   readonly List<FileStream> held=new List<FileStream>();readonly Dictionary<string,Dictionary<string,object>> data=new Dictionary<string,Dictionary<string,object>>(StringComparer.OrdinalIgnoreCase);readonly Dictionary<string,string> hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   internal Dictionary<string,object> Json(string path,string expected,int limit=65536) {
    path=OwnedCopies.LocalPath(path);OwnedCopies.NoReparse(path);Dictionary<string,object> cached;if(data.TryGetValue(path,out cached)){if(expected!=null&&hashes[path]!=expected)throw new IOException("Cached proof SHA differs.");return cached;}
    var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);held.Add(stream);if(stream.Length>limit)throw new IOException("Proof artifact size limit.");byte[] bytes;using(var memory=new MemoryStream()){stream.CopyTo(memory);bytes=memory.ToArray();}
    using(var hash=SHA256.Create()){string sha=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();if(expected!=null&&sha!=expected)throw new IOException("Proof artifact SHA differs.");hashes[path]=sha;}
    cached=Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));if(cached==null)throw new IOException("Object proof required.");data[path]=cached;return cached;
   }
   internal int Count {get{return held.Count;}}
   public void Dispose(){foreach(var stream in held)stream.Dispose();}
  }
  public static void ValidateParameters(Dictionary<string,object> args,bool prepare) {
   string[] fields=prepare?new[]{"operationPlanId","operationPlanSha256","bindingId","bindingSha256"}:new[]{"ownedOperationId","ownedOperationSha256"};
   if(args.Count!=fields.Length||fields.Any(k=>!args.ContainsKey(k)||!EntitySets.Hex(args[k],k.EndsWith("Sha256",StringComparison.Ordinal)?64:32)))throw new ArgumentException("Invalid coupled operation proof identity.");
  }
  static void Fields(Dictionary<string,object> value,params string[] fields){if(value.Count!=fields.Length||fields.Any(k=>!value.ContainsKey(k)))throw new IOException("Proof schema differs.");}
  static Dictionary<string,object> Identity(Dictionary<string,object> receipt){return new[]{"processId","processStartedUtc","windowsSessionId","productVersion","model"}.ToDictionary(k=>k,k=>receipt[k]);}
  static string Artifact(string root,string folder,object id){if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid artifact identity.");return Path.Combine(root,folder,(string)id+".json");}
  static object[] Values(object value){var list=value as IList;if(list==null)throw new ArgumentException("Explicit list required.");return list.Cast<object>().ToArray();}
  static double Number(object value){if(!(value is int||value is long||value is double||value is float||value is decimal))throw new ArgumentException("Numeric operation value required.");return Program.StrictDouble(value);}
  static void Parameters(Dictionary<string,object> plan) {
   string kind=(string)plan["operation"];var args=plan["parameters"] as Dictionary<string,object>;if(args==null)throw new ArgumentException("Explicit operation parameters required.");
   if(kind=="translate_entities") {
    Fields(args,"vector","coordinateFrame","lengthUnits","mode");if(!Equals(args["coordinateFrame"],"world")||!Equals(args["lengthUnits"],"document_raw")||!Equals(args["mode"],"move"))throw new ArgumentException("Unsupported translation parameters.");
    var vector=Values(args["vector"]);if(vector.Length!=3)throw new ArgumentException("Three vector values required.");var numbers=vector.Select(Number).ToArray();if(numbers.Any(n=>double.IsNaN(n)||double.IsInfinity(n)||Math.Abs(n)>1000000)||numbers.All(n=>n==0))throw new ArgumentException("Invalid translation vector.");
   }else if(kind=="intersect_entity_sets"){Fields(args,"offset");if(Number(args["offset"])!=0)throw new ArgumentException("Only zero offset is qualified for planning.");}
   else throw new ArgumentException("Unsupported operation kind.");
  }
  internal static void Prepared(string root,string id,string sha,Proofs proofs) {
   string folder=Path.Combine(root,"operations",id);OwnedCopies.NoReparse(folder);
   if(File.Exists(Path.Combine(folder,"writer.lock"))||Directory.GetFiles(folder,"*.json").Length!=1)throw new IOException("Only intact prepared journal accepted; no repair or replay.");
   var e=proofs.Json(Path.Combine(folder,"000000.json"),null);Fields(e,"schemaVersion","operationId","planSha256","sequence","previousSha256","state","timestampUtc","details");
   if(!Equals(e["schemaVersion"],1)||!Equals(e["operationId"],id)||!Equals(e["planSha256"],sha)||!Equals(e["sequence"],0)||e["previousSha256"]!=null||!Equals(e["state"],"prepared"))throw new IOException("Prepared journal identity/state differs.");
   NativeJournal.Transition(null,"prepared",(Dictionary<string,object>)e["details"]);EntitySets.Expiry(e["timestampUtc"]);
  }
  internal sealed class Dependencies {
   internal Dictionary<string,object> Plan,Binding,Expected;internal DateTime Expires;internal string Digest;internal int[][] Keys;
   internal Dependencies(string root,Dictionary<string,object> args,Proofs proofs) {
    ValidateParameters(args,true);Plan=proofs.Json(Artifact(root,"operationplans",args["operationPlanId"]),(string)args["operationPlanSha256"]);
    Fields(Plan,"schemaVersion","operationId","operation","parameters","preparedUtc","expiresUtc","executionEnabled","sourceIdentity","observedPropertiesSha256","observationDigestEncoding","lengthUnitsRaw","lengthUnitFactorRaw","entitySets","requiredBeforeExecution","qualification");
    if(!Equals(Plan["schemaVersion"],1)||!Equals(Plan["operationId"],args["operationPlanId"])||!Equals(Plan["executionEnabled"],false)||!EntitySets.Hex(Plan["observedPropertiesSha256"],64)||!Equals(Plan["observationDigestEncoding"],"Python JSON sorted keys; distinct from native C# digest")||EntitySets.Canonical(Plan["requiredBeforeExecution"])!=EntitySets.Canonical(new[]{"qualified_operation","owned_working_copy","independent_checkpoint","guard_in_executor_com_context","postconditions"}))throw new IOException("Local plan policy differs.");Parameters(Plan);
    Prepared(root,(string)args["operationPlanId"],(string)args["operationPlanSha256"],proofs);
    var bindingArgs=new Dictionary<string,object>{{"bindingId",args["bindingId"]},{"bindingSha256",args["bindingSha256"]}};var validated=DocumentBindings.Load(root,bindingArgs);
    Binding=proofs.Json(Artifact(root,"documentbindings",args["bindingId"]),(string)args["bindingSha256"]);if(EntitySets.Canonical(validated)!=EntitySets.Canonical(Binding))throw new IOException("Binding changed during load.");
    Expected=proofs.Json(Artifact(root,"responses",Binding["inspectionRequestId"]),(string)Binding["inspectionSha256"],16000000);Digest=EntitySets.ObservedDigest(Expected);
    if(Digest!=(string)Binding["observedPropertiesSha256"]||EntitySets.Canonical(Identity(Expected))!=EntitySets.Canonical(Plan["sourceIdentity"])||EntitySets.Canonical(Expected["lengthUnitsRaw"])!=EntitySets.Canonical(Plan["lengthUnitsRaw"])||EntitySets.Canonical(Expected["lengthUnitFactorRaw"])!=EntitySets.Canonical(Plan["lengthUnitFactorRaw"]))throw new IOException("Plan and binding document/units differ.");
    foreach(string key in new[]{"contextBefore","contextAfter"})if(EntitySets.Canonical(Expected[key])!=EntitySets.Canonical(new[]{0,0}))throw new InvalidOperationException("Empty selection and world workplane required.");
    foreach(string key in new[]{"selectionBefore","selectionAfter"})if(Values(Expected[key]).Length!=0)throw new InvalidOperationException("Empty entity selection required.");
    var sets=Values(Plan["entitySets"]);if(sets.Length!=((string)Plan["operation"]=="translate_entities"?1:2))throw new ArgumentException("Operation set count differs.");
    var setIds=new HashSet<string>();var keys=new List<int[]>();DateTime setExpiry=DateTime.MaxValue;var roles=new List<int[][]>();
    foreach(var item in sets) {
     var entry=item as Dictionary<string,object>;if(entry==null)throw new IOException("Invalid set record.");Fields(entry,"entitySetId","entitySetSha256","inspectionRequestId","inspectionSha256","expiresUtc","entityKeys");
     EntitySets.ValidateHandle(new Dictionary<string,object>{{"entitySetId",entry["entitySetId"]},{"entitySetSha256",entry["entitySetSha256"]}});if(!setIds.Add((string)entry["entitySetId"]))throw new ArgumentException("Duplicate entity set role.");
     var set=proofs.Json(Artifact(root,"entitysets",entry["entitySetId"]),(string)entry["entitySetSha256"]);EntitySets.ValidatePrepare(set);
     foreach(string key in set.Keys)if(EntitySets.Canonical(set[key])!=EntitySets.Canonical(entry[key]))throw new IOException("Plan set differs from immutable set.");
     var source=proofs.Json(Artifact(root,"responses",set["inspectionRequestId"]),(string)set["inspectionSha256"],16000000);var selected=EntitySets.Keys(set["entityKeys"]);var expiry=EntitySets.Expiry(set["expiresUtc"]);EntitySets.CheckObserved(source,Expected,selected,expiry,DateTime.UtcNow);
     if(EntitySets.Expiry(Plan["preparedUtc"])<EntitySets.Expiry(source["timestampUtc"]))throw new IOException("Plan predates source observation.");setExpiry=expiry<setExpiry?expiry:setExpiry;keys.AddRange(selected);roles.Add(selected);
    }
    if((string)Plan["operation"]=="intersect_entity_sets"&&(roles[0].Any(k=>k[0]!=40&&k[0]!=21)||roles[1].Any(k=>k[0]!=21)||roles[0].Any(k=>roles[1].Any(other=>k.SequenceEqual(other)))))throw new ArgumentException("Intersection roles/types/overlap rejected.");
    if(EntitySets.Expiry(Plan["expiresUtc"])!=setExpiry||EntitySets.Expiry(Plan["preparedUtc"])>DateTime.UtcNow.AddSeconds(5))throw new IOException("Plan lifetime differs from sets.");
    Expires=new[]{setExpiry,EntitySets.Expiry(Binding["expiresUtc"])}.Min();EntitySets.CheckObserved(Expected,Expected,new int[0][],Expires,DateTime.UtcNow);Keys=keys.ToArray();
   }
   internal void Check(OwnedCopies.OwnershipLease lease,DocumentBindings.IContext context,Dictionary<string,object> diagnostics) {
    // Same expected inventory proves each selected key; fresh reads use the held document.
    EntitySets.CheckObserved(Expected,Expected,Keys,Expires,DateTime.UtcNow);
    DocumentBindings.Check(lease,Expected,Expires,context,diagnostics,(string)Binding["windowContextSha256"]);
   }
  }
  internal static Dictionary<string,object> Packet(string root,Dictionary<string,object> args,Proofs proofs) {
   ValidateParameters(args,false);var packet=proofs.Json(Artifact(root,"ownedoperations",args["ownedOperationId"]),(string)args["ownedOperationSha256"]);
   Fields(packet,"schemaVersion","ownedOperationId","operationPlanId","operationPlanSha256","bindingId","bindingSha256","preparedUtc","expiresUtc","executionEnabled","nativeObservedPropertiesSha256","windowContextSha256","workingSha256","copyId","ownershipSha256");
   if(!Equals(packet["schemaVersion"],1)||!Equals(packet["ownedOperationId"],args["ownedOperationId"])||!Equals(packet["executionEnabled"],false)||!EntitySets.Hex(packet["workingSha256"],64)||!EntitySets.Hex(packet["nativeObservedPropertiesSha256"],64))throw new IOException("Coupled packet policy differs.");
   Prepared(root,(string)args["ownedOperationId"],(string)args["ownedOperationSha256"],proofs);return packet;
  }
  internal static Dictionary<string,object> InContext(string root,string id,Dictionary<string,object> args,bool prepare,DocumentBindings.IContext context) {
   var result=new Dictionary<string,object>{{"state","owned_operation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"executionEnabled",false},{"qualification",Qualification}};
   try {if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid native request identity.");using(var proofs=new Proofs()) {
    var packet=prepare?null:Packet(root,args,proofs);var parameters=prepare?args:new[]{"operationPlanId","operationPlanSha256","bindingId","bindingSha256"}.ToDictionary(k=>k,k=>packet[k]);var dependencies=new Dependencies(root,parameters,proofs);
    using(var lease=OwnedCopies.Hold(root,(string)dependencies.Binding["copyId"],(string)dependencies.Binding["ownershipSha256"])) {
     if(packet!=null&&(EntitySets.Expiry(packet["expiresUtc"])!=dependencies.Expires||!Equals(packet["nativeObservedPropertiesSha256"],dependencies.Digest)||!Equals(packet["windowContextSha256"],dependencies.Binding["windowContextSha256"])||!Equals(packet["copyId"],dependencies.Binding["copyId"])||!Equals(packet["ownershipSha256"],dependencies.Binding["ownershipSha256"])||!Equals(packet["workingSha256"],lease.Inspection["workingSha256"])))throw new IOException("Coupled dependency identity or working bytes changed.");
     dependencies.Check(lease,context,result);
     string packetId=prepare?id:(string)args["ownedOperationId"],sha=prepare?null:(string)args["ownedOperationSha256"];
     if(prepare) {
      packet=new Dictionary<string,object>(parameters){{"schemaVersion",1},{"ownedOperationId",packetId},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",dependencies.Expires.ToString("o")},{"executionEnabled",false},{"nativeObservedPropertiesSha256",dependencies.Digest},{"windowContextSha256",dependencies.Binding["windowContextSha256"]},{"workingSha256",lease.Inspection["workingSha256"]},{"copyId",dependencies.Binding["copyId"]},{"ownershipSha256",dependencies.Binding["ownershipSha256"]}};
      string path=Artifact(root,"ownedoperations",packetId);Directory.CreateDirectory(Path.GetDirectoryName(path));result["ownedOperationId"]=packetId;result["localArtifactsMayExist"]=true;sha=NativeJournal.Freeze(path,packet);result["ownedOperationSha256"]=sha;
      var journal=new NativeJournal(root,packetId,sha);journal.Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"geometryMutationAttempted",false},{"operationPlanId",parameters["operationPlanId"]},{"bindingId",parameters["bindingId"]}});Packet(root,new Dictionary<string,object>{{"ownedOperationId",packetId},{"ownedOperationSha256",sha}},proofs);
     }
     dependencies.Check(lease,context,result);Prepared(root,(string)parameters["operationPlanId"],(string)parameters["operationPlanSha256"],proofs);Prepared(root,packetId,sha,proofs);lease.Recheck();context.ConfirmDocument();if(DocumentBindings.WindowDigest(context.Windows())!=(string)dependencies.Binding["windowContextSha256"]||DateTime.UtcNow>=dependencies.Expires)throw new InvalidOperationException("Final coupled context/expiry changed.");
     result["state"]=prepare?"owned_operation_prepared_observed":"owned_operation_validated_observed";result["ownedOperationId"]=packetId;result["ownedOperationSha256"]=sha;result["ownedOperation"]=packet;result["expiresUtc"]=packet["expiresUtc"];result["operationPlanId"]=parameters["operationPlanId"];result["bindingId"]=parameters["bindingId"];result["copyId"]=packet["copyId"];result["guardPasses"]=2;result["sameContextObservationReads"]=4;result["heldProofArtifactCount"]=proofs.Count;result["journalPreparedOnly"]=true;
    }
   }}catch(Exception error){result["error"]=error.Message;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
  public static Dictionary<string,object> Run(string id,Dictionary<string,object> args,bool prepare) {
   try {
    Config config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedOperationPlansExperimental)throw new InvalidOperationException("Coupled plans disabled.");ValidateParameters(args,prepare);
    Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var context=new DocumentBindings.ComContext(config))return InContext(Program.Root,id,args,prepare,context);
   }catch(Exception error){return new Dictionary<string,object>{{"state","owned_operation_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"error",error.Message},{"geometryMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"executionEnabled",false}};}
  }
 }
}
