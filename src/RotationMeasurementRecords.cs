using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 // Immutable local witness records. Creating a record never grants verification/fence authority.
 internal static class RotationMeasurementRecords {
  static void Require(bool ok,string error){if(!ok)throw new InvalidDataException(error);}
  static void Fields(Dictionary<string,object> value,params string[] fields){Require(value.Count==fields.Length&&fields.All(value.ContainsKey),"Measurement record fields differ.");}
  static object[] Values(object value){var array=value as IList;Require(array!=null,"Explicit witness array required.");return array.Cast<object>().ToArray();}
  static double[] Numbers(object value){return Values(value).Select(v=>{Require(v is int||v is long||v is double||v is float||v is decimal,"Numeric witness required.");double number=Program.StrictDouble(v);Require(!double.IsNaN(number)&&!double.IsInfinity(number),"Finite witness required.");return number;}).ToArray();}
  static string Text(Dictionary<string,object> value,string key){Require(value.ContainsKey(key)&&value[key] is string,"Measurement text required.");return (string)value[key];}
  internal static string PathFor(string root,string executionId,int index,string phase){Require(EntitySets.Hex(executionId,32)&&index>=0&&index<128&&new[]{"before","after"}.Contains(phase),"Fixed measurement record identity required.");string path=Path.Combine(root,"rotationmeasurements",executionId,index.ToString("D6")+"."+phase+".json");OwnedCopies.NoReparse(path);return path;}
  static object[] Witnesses(RotationSampleVerifier.Witness[] values){return values.Select(v=>(object)new Dictionary<string,object>{{"key",v.Key},{"value",v.Value}}).ToArray();}
  internal static Dictionary<string,object> Encode(RotationSampleVerifier.Samples samples) {
   RotationSampleVerifier.Validate(samples);return new Dictionary<string,object>{{"type",samples.Type},{"nativeId",samples.NativeId},{"frame",samples.Frame},{"units",samples.Units},{"descriptorSha256",samples.DescriptorSha256},{"positions",Witnesses(samples.Positions)},{"directions",Witnesses(samples.Directions)},{"invariants",samples.Invariants},{"boundsWorldRawPaired",samples.BoundsPaired}};
  }
  static RotationSampleVerifier.Witness[] Witnesses(object value){return Values(value).Select(v=>{var row=v as Dictionary<string,object>;Require(row!=null,"Object witness required.");Fields(row,"key","value");return new RotationSampleVerifier.Witness(Text(row,"key"),Numbers(row["value"]));}).ToArray();}
  internal static RotationSampleVerifier.Samples Decode(Dictionary<string,object> value) {
   Fields(value,"type","nativeId","frame","units","descriptorSha256","positions","directions","invariants","boundsWorldRawPaired");Require(value["type"] is int&&value["nativeId"] is int,"Native int witness identity required.");
   var result=new RotationSampleVerifier.Samples((int)value["type"],(int)value["nativeId"],Text(value,"frame"),Text(value,"units"),Text(value,"descriptorSha256"),Witnesses(value["positions"]),Witnesses(value["directions"]),Numbers(value["invariants"]),Numbers(value["boundsWorldRawPaired"]));RotationSampleVerifier.Validate(result);return result;
  }
  internal static Dictionary<string,object> Provenance(Dictionary<string,object> packet,OwnedRotationPlans.Dependencies dependencies) {
   return new Dictionary<string,object>{{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"workingSha256",packet["workingSha256"]},{"bindingId",packet["bindingId"]},{"bindingSha256",packet["bindingSha256"]},{"inspectionRequestId",dependencies.Binding["inspectionRequestId"]},{"inspectionSha256",dependencies.Binding["inspectionSha256"]},{"windowContextSha256",packet["windowContextSha256"]},{"sourceObservedPropertiesSha256",packet["nativeObservedPropertiesSha256"]},{"expiresUtc",packet["expiresUtc"]}};
  }
  internal static string Freeze(string root,string id,int index,string phase,Dictionary<string,object> provenance,RotationSampleVerifier.Samples samples) {
   string path=PathFor(root,id,index,phase);Directory.CreateDirectory(Path.GetDirectoryName(path));var record=new Dictionary<string,object>{{"schemaVersion",1},{"executionRequestId",id},{"index",index},{"phase",phase},{"recordedUtc",DateTime.UtcNow.ToString("o")},{"provenance",provenance},{"samples",Encode(samples)},{"qualification","experimental observed witnesses; not full topology or native convention proof"}};return NativeJournal.Freeze(path,record);
  }
  internal static RotationSampleVerifier.Samples Read(string root,string id,int index,string phase,string sha,Dictionary<string,object> provenance,OwnedOperationPlans.Proofs proofs) {
   Require(EntitySets.Hex(sha,64),"Pinned measurement SHA required.");var row=proofs.Json(PathFor(root,id,index,phase),sha,16000000);Fields(row,"schemaVersion","executionRequestId","index","phase","recordedUtc","provenance","samples","qualification");
   Require(Equals(row["schemaVersion"],1)&&Equals(row["executionRequestId"],id)&&Equals(row["index"],index)&&Equals(row["phase"],phase)&&EntitySets.Canonical(row["provenance"])==EntitySets.Canonical(provenance)&&Equals(row["qualification"],"experimental observed witnesses; not full topology or native convention proof"),"Measurement identity/provenance differs.");
   Require(EntitySets.Expiry(row["recordedUtc"])<=DateTime.UtcNow.AddSeconds(5),"Measurement timestamp invalid.");return Decode((Dictionary<string,object>)row["samples"]);
  }
 }
}
