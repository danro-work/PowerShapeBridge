using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerShapeBridge {
 // Experimental read/preparation only. No selection, activation, save or geometry commands.
 public static class EntitySets {
  public const string Qualification="Observed properties only; not full topology or persistent identity. Standalone validation does not authorize a later edit. No mutation tools accept this set yet.";
  static readonly string[] Fields={"processId","processStartedUtc","windowsSessionId","productVersion","model","entityCount","entities","levelsContainingEntities","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"};

  public static bool Hex(object value,int length) {
   var text=value as string;return text!=null&&text.Length==length&&text.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));
  }
  public static string Canonical(object value) {
   var dict=value as IDictionary<string,object>;
   if(dict!=null)return "{"+string.Join(",",dict.Keys.OrderBy(x=>x,StringComparer.Ordinal).Select(k=>Program.Json.Serialize(k)+":"+Canonical(dict[k])))+"}";
   if(value is string || value==null || value is bool)return Program.Json.Serialize(value);
   var sequence=value as IEnumerable;
   if(sequence!=null)return "["+string.Join(",",sequence.Cast<object>().Select(Canonical))+"]";
   if(value is double || value is float || value is decimal || value is int || value is long || value is short || value is byte) {
    double number=Program.StrictDouble(value);return number.ToString("R",CultureInfo.InvariantCulture);
   }
   throw new ArgumentException("Unsupported observation value.");
  }
  static string Digest(object value) {
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Canonical(value)))).Replace("-","").ToLowerInvariant();
  }
  static object[] Values(object value) {
   if(!(value is IList))throw new ArgumentException("Explicit list required.");return ((IEnumerable)value).Cast<object>().ToArray();
  }
  public static Dictionary<string,object>[] Inventory(Dictionary<string,object> snapshot) {
   if(!snapshot.ContainsKey("state")||!Equals(snapshot["state"],"succeeded_verified")||!snapshot.ContainsKey("modelScope")||!Equals(snapshot["modelScope"],"active_document_read_only"))throw new InvalidOperationException("Verified active-document read required.");
   var entities=Values(snapshot["entities"]).Select(x=>x as Dictionary<string,object>).ToArray();
   if(entities.Length>4096||!snapshot.ContainsKey("entityCount")||!(snapshot["entityCount"] is int)||entities.Length!=(int)snapshot["entityCount"]||entities.Any(e=>e==null))throw new InvalidOperationException("Incomplete inventory.");
   var keys=new HashSet<string>();
   foreach(var entity in entities) {
    if(!entity.ContainsKey("nativeTypeCode")||!(entity["nativeTypeCode"] is int)||!entity.ContainsKey("nativeId")||!(entity["nativeId"] is int)||(int)entity["nativeTypeCode"]<0||(int)entity["nativeId"]<0||!keys.Add(entity["nativeTypeCode"]+":"+entity["nativeId"]))throw new InvalidOperationException("Ambiguous native entity identity.");
   }
   return entities;
  }
  public static string ObservedDigest(Dictionary<string,object> snapshot) {
   // Probe contains anonymous DTOs (notably model); compare the same JSON data graph as receipts.
   snapshot=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(snapshot));
   var entities=Inventory(snapshot);var observed=new Dictionary<string,object>();
   foreach(string field in Fields) {if(!snapshot.ContainsKey(field))throw new InvalidOperationException("Incomplete observation: "+field);observed[field]=snapshot[field];}
   observed["entities"]=entities.OrderBy(e=>(int)e["nativeTypeCode"]).ThenBy(e=>(int)e["nativeId"]).ToArray();
   var model=snapshot["model"] as Dictionary<string,object>;
   if(model==null||!model.ContainsKey("id")||!(model["id"] is int)||(int)model["id"]<0||!model.ContainsKey("path")||!(model["path"] is string)||!model.ContainsKey("name")||!(model["name"] is string))throw new InvalidOperationException("Incomplete model identity.");
   foreach(string field in new[]{"processId","windowsSessionId"})if(!(snapshot[field] is int)||(int)snapshot[field]<0)throw new InvalidOperationException("Incomplete process identity.");
   if(!(snapshot["processStartedUtc"] is string)||string.IsNullOrWhiteSpace((string)snapshot["processStartedUtc"]))throw new InvalidOperationException("Incomplete process start identity.");
   return Digest(observed);
  }
  public static DateTime Expiry(object value) {
   if(!(value is string))throw new ArgumentException("Invalid entity-set expiry.");
   DateTimeOffset expiry;
   if(!DateTimeOffset.TryParseExact((string)value,new[]{"o","yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"},CultureInfo.InvariantCulture,DateTimeStyles.None,out expiry)||!((string)value).EndsWith("Z",StringComparison.Ordinal)&&((string)value).IndexOf('+',10)<0)throw new ArgumentException("Expiry requires explicit UTC offset.");
   return expiry.UtcDateTime;
  }
  public static int[][] Keys(object value) {
   var values=value as IEnumerable;if(values==null||value is string)throw new ArgumentException("Explicit entity keys required.");
   var keys=new List<int[]>();var seen=new HashSet<string>();
   foreach(object row in values) {
    var pair=Values(row);if(pair.Length!=2||pair.Any(x=>!(x is int)))throw new ArgumentException("Invalid native entity key.");
    int type=(int)pair[0],id=(int)pair[1];if(!new[]{40,21,59}.Contains(type)||id<0||!seen.Add(type+":"+id))throw new ArgumentException("Duplicate or unsupported entity key.");
    keys.Add(new[]{type,id});if(keys.Count>128)throw new ArgumentException("Entity set exceeds limit.");
   }
   if(keys.Count==0)throw new ArgumentException("Entity set must be nonempty.");return keys.ToArray();
  }
  public static void ValidatePrepare(Dictionary<string,object> args) {
   if(args.Count!=4||!args.ContainsKey("inspectionRequestId")||!Hex(args["inspectionRequestId"],32)||!args.ContainsKey("inspectionSha256")||!Hex(args["inspectionSha256"],64)||!args.ContainsKey("expiresUtc")||!args.ContainsKey("entityKeys"))throw new ArgumentException("Invalid entity-set preparation parameters.");
   Expiry(args["expiresUtc"]);Keys(args["entityKeys"]);
  }
  public static void ValidateHandle(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("entitySetId")||!Hex(args["entitySetId"],32)||!args.ContainsKey("entitySetSha256")||!Hex(args["entitySetSha256"],64))throw new ArgumentException("Invalid entity-set identity.");
  }
  internal static Dictionary<string,object> Receipt(string id,string sha) {
   return Receipt(Program.Root,id,sha);
  }
  internal static Dictionary<string,object> Receipt(string root,string id,string sha) {
   string path=Path.Combine(root,"responses",id+".json");
   using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(input.Length>16000000)throw new IOException("Inspection receipt exceeds limit.");
    byte[] bytes=new byte[(int)input.Length];int offset=0;while(offset<bytes.Length){int n=input.Read(bytes,offset,bytes.Length-offset);if(n==0)throw new IOException("Incomplete receipt read.");offset+=n;}
    using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=sha)throw new IOException("Inspection receipt changed.");
    return Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));
   }
  }
  public static void CheckObserved(Dictionary<string,object> expected,Dictionary<string,object> fresh,int[][] keys,DateTime expiry,DateTime now) {
   var issued=Expiry(expected["timestampUtc"]);
   if(expiry<=now||expiry>issued.AddSeconds(300)||expiry<issued||issued>now.AddSeconds(5))throw new InvalidOperationException("Entity-set observation expired or invalid.");
   if(ObservedDigest(expected)!=ObservedDigest(fresh))throw new InvalidOperationException("Observed document/context/properties changed.");
   var entities=Inventory(fresh);
   foreach(var key in keys)if(entities.Count(e=>Equals(e["nativeTypeCode"],key[0])&&Equals(e["nativeId"],key[1]))!=1)throw new InvalidOperationException("Entity absent or ambiguous.");
  }
  public static Dictionary<string,object> Run(string requestId,Dictionary<string,object> args,bool prepare) {
   var result=new Dictionary<string,object>{{"state","entity_set_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"cadMutationAttempted",false},{"entitySetQualification",Qualification}};
   try {
    if(!Hex(requestId,32))throw new ArgumentException("Invalid entity-set request identity.");
    if(!Program.ReadConfig().IsGeneralCore||!Program.ReadConfig().entitySetsExperimental)throw new InvalidOperationException("Experimental entity sets disabled.");
    if(prepare)ValidatePrepare(args);else ValidateHandle(args);
    string folder=Path.Combine(Program.Root,"entitysets");
    Dictionary<string,object> plan;
    if(prepare)plan=new Dictionary<string,object>(args);
    else {
     string path=Path.Combine(folder,(string)args["entitySetId"]+".json");plan=ReceiptPlan(path,(string)args["entitySetSha256"]);ValidatePrepare(plan);
    }
    var expected=Receipt((string)plan["inspectionRequestId"],(string)plan["inspectionSha256"]);DateTime expiry=Expiry(plan["expiresUtc"]);int[][] keys=Keys(plan["entityKeys"]);
    // Cheap receipt/expiry/key rejection precedes any CAD read.
    CheckObserved(expected,expected,keys,expiry,DateTime.UtcNow);
    var fresh=Probe.Run(false,false);
    CheckObserved(expected,fresh,keys,expiry,DateTime.UtcNow);
    result["observedPropertiesSha256"]=ObservedDigest(fresh);result["model"]=fresh["model"];result["processId"]=fresh["processId"];result["processStartedUtc"]=fresh["processStartedUtc"];result["windowsSessionId"]=fresh["windowsSessionId"];
    result["entities"]=Inventory(fresh).Where(e=>keys.Any(k=>Equals(e["nativeTypeCode"],k[0])&&Equals(e["nativeId"],k[1]))).ToArray();
    result["expiresUtc"]=plan["expiresUtc"];result["inspectionRequestId"]=plan["inspectionRequestId"];
    if(prepare) {
     Directory.CreateDirectory(folder);string id=requestId,path=Path.Combine(folder,id+".json");
     using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file,new UTF8Encoding(false))){writer.Write(Program.Json.Serialize(plan));writer.Flush();file.Flush(true);}
     result["entitySetId"]=id;result["entitySetSha256"]=Program.Hash(path);result["state"]="entity_set_prepared_observed";
    } else {result["entitySetId"]=args["entitySetId"];result["state"]="entity_set_validated_observed";}
   }catch(Exception error){result["error"]=error.Message;}
   return result;
  }
  static Dictionary<string,object> ReceiptPlan(string path,string sha) {
   // Read/hash the same bytes; never reopen between verification and parsing.
   byte[] bytes;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if(file.Length>65536)throw new IOException("Entity-set plan exceeds limit.");
    using(var memory=new MemoryStream()){file.CopyTo(memory);bytes=memory.ToArray();}
   }
   using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=sha)throw new IOException("Entity-set plan changed.");
   return Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
  }
  public static int SelfTest() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Entity-set self-test failed at "+count);count++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(FormatException){rejected=true;}check(rejected);};
   var snapshot=new Dictionary<string,object>();foreach(var field in Fields)snapshot[field]=null;
   snapshot["state"]="succeeded_verified";snapshot["modelScope"]="active_document_read_only";snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");
   snapshot["processId"]=1;snapshot["processStartedUtc"]="2026-10-09T10:00:00Z";snapshot["windowsSessionId"]=1;snapshot["productVersion"]="23.2.2";
   snapshot["model"]=new Dictionary<string,object>{{"id",9},{"path","D:/Part.psmodel"},{"name","Part"}};
   snapshot["entities"]=new List<Dictionary<string,object>>{new Dictionary<string,object>{{"nativeTypeCode",40},{"nativeId",5},{"name","Body"},{"type","SOLID"},{"levelNumber",0},{"geometry",new Dictionary<string,object>{{"volumeRaw",1.0}}}}};snapshot["entityCount"]=1;
   snapshot["contextBefore"]=new[]{0,0};snapshot["contextAfter"]=new[]{0,0};snapshot["selectionBefore"]=new object[0];snapshot["selectionAfter"]=new object[0];snapshot["workplanes"]=new object[0];snapshot["levelsContainingEntities"]=new object[0];
   snapshot["lengthUnitsRaw"]="mm";snapshot["lengthUnitFactorRaw"]=1.0;
   Func<Dictionary<string,object>> clone=()=>Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(snapshot));
   var keys=new[]{new[]{40,5}};var expiry=DateTime.UtcNow.AddSeconds(200);
   CheckObserved(snapshot,clone(),keys,expiry,DateTime.UtcNow);count++;
   check(ObservedDigest(snapshot)==ObservedDigest(clone()));
   var nativeShape=clone();nativeShape["model"]=new{id=9,path="D:/Part.psmodel",name="Part"};
   check(ObservedDigest(nativeShape)==ObservedDigest(snapshot));
   foreach(string field in new[]{"processId","processStartedUtc","windowsSessionId","model","entities","lengthUnitFactorRaw","contextBefore","selectionBefore","workplanes","levelsContainingEntities"}) {
    var changed=clone();changed[field]=null;rejects(()=>CheckObserved(snapshot,changed,keys,expiry,DateTime.UtcNow));
   }
   var bodyChange=clone();var body=(Dictionary<string,object>)Values(bodyChange["entities"])[0];body["name"]="Renamed";rejects(()=>CheckObserved(snapshot,bodyChange,keys,expiry,DateTime.UtcNow));
   bodyChange=clone();body=(Dictionary<string,object>)Values(bodyChange["entities"])[0];body["geometry"]=new Dictionary<string,object>{{"volumeRaw",2.0}};rejects(()=>CheckObserved(snapshot,bodyChange,keys,expiry,DateTime.UtcNow));
   rejects(()=>CheckObserved(snapshot,snapshot,new[]{new[]{40,99}},expiry,DateTime.UtcNow));
   rejects(()=>CheckObserved(snapshot,snapshot,keys,DateTime.UtcNow.AddSeconds(-1),DateTime.UtcNow));
   rejects(()=>CheckObserved(snapshot,snapshot,keys,DateTime.UtcNow.AddSeconds(301),DateTime.UtcNow));
   foreach(object invalid in new object[]{new int[0][],new[]{new[]{40,5},new[]{40,5}},new[]{new[]{54,5}},new[]{new[]{40,-1}},new object[]{new object[]{true,5}},new object[]{new object[]{"40",5}},"40:5"})rejects(()=>Keys(invalid));
   var duplicates=clone();duplicates["entities"]=new[]{body,body};duplicates["entityCount"]=2;rejects(()=>ObservedDigest(duplicates));
   var failed=clone();failed["state"]="failed";rejects(()=>ObservedDigest(failed));
   rejects(()=>Expiry("2026-10-09T10:00:00"));
   var parameters=new Dictionary<string,object>{{"inspectionRequestId",new string('a',32)},{"inspectionSha256",new string('b',64)},{"entityKeys",keys},{"expiresUtc",expiry.ToString("o")}};
   ValidatePrepare(parameters);count++;
   foreach(string extra in new[]{"command","path","documentReference"}) {var bad=new Dictionary<string,object>(parameters);bad[extra]="arbitrary";rejects(()=>ValidatePrepare(bad));}
   var handle=new Dictionary<string,object>{{"entitySetId",new string('a',32)},{"entitySetSha256",new string('b',64)}};ValidateHandle(handle);count++;
   handle["entitySetId"]="../outside";rejects(()=>ValidateHandle(handle));
   check(Canonical(new Dictionary<string,object>{{"z",1.0},{"a",new[]{2.0,3.0}}})==Canonical(new Dictionary<string,object>{{"a",new[]{2,3}},{"z",1}}));
   foreach(string field in new[]{"id","path","name"}) {
    var changed=clone();var model=(Dictionary<string,object>)changed["model"];model[field]=field=="id"?(object)10:"Other";rejects(()=>CheckObserved(snapshot,changed,keys,expiry,DateTime.UtcNow));
   }
   var receiptFolder=Path.Combine(Program.Root,"work","entity-set-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(receiptFolder);
   string planPath=Path.Combine(receiptFolder,"plan.json");File.WriteAllText(planPath,Program.Json.Serialize(parameters),new UTF8Encoding(false));
   string frozenSha=Program.Hash(planPath);check(Canonical(ReceiptPlan(planPath,frozenSha))==Canonical(parameters));
   File.WriteAllText(planPath,"{}",new UTF8Encoding(false));bool tampered=false;try{ReceiptPlan(planPath,frozenSha);}catch(IOException){tampered=true;}check(tampered);
   if(Program.ReadConfig().entitySetsExperimental) {
    string id=new string('a',32);
    foreach(string recipe in new[]{"prepare_entity_set","validate_entity_set"}) {
     var args=recipe=="prepare_entity_set"?parameters:new Dictionary<string,object>{{"entitySetId",id},{"entitySetSha256",new string('b',64)}};
     FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="recipe.prepare",arguments=new{recipeId=recipe,parameters=args}}));count++;
    }
    rejects(()=>FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="recipe.prepare",arguments=new{recipeId="prepare_entity_set",parameters=new{entityKeys=keys}}})));
   }
   return count;
  }
 }
}
