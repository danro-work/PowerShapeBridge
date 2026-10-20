using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class OwnedRotationExecutionTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Fake : OwnedRotationExecution.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal int Calls,Reads,MeasurementCalls;internal string Fault;
   internal readonly Dictionary<int,double[]> Boxes=new Dictionary<int,double[]>();internal readonly List<Dictionary<string,object>> Commands=new List<Dictionary<string,object>>();
   internal Fake(OwnedRotationPlanTests.Fixture fixture,string fault){Snapshot=Clone(fixture.Base.Snapshot);Listing=Clone(fixture.Base.Listing);Fault=fault;foreach(var entity in EntitySets.Inventory(Snapshot)){int key=(int)entity["nativeTypeCode"];Boxes[key]=new double[]{key,key+2,0,3,-1,4};}}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public Dictionary<string,object> Observation(){Reads++;if(Fault=="late_context"&&Reads==4||Fault=="after_start_context"&&Reads==10)Snapshot["processId"]=99;if(Fault=="previous_target_changed"&&Reads==15)EntitySets.Inventory(Snapshot).Single(e=>Equals(e["nativeTypeCode"],40))["geometry"]=new Dictionary<string,object>{{"syntheticEffect",99}};var value=Clone(Snapshot);if(Fault=="native_dto"){var model=(Dictionary<string,object>)value["model"];value["model"]=new{id=(int)model["id"],name=(string)model["name"],path=(string)model["path"]};}return value;}
   public void ConfirmDocument(){}
   public int Composite(int type,int id){Require(EntitySets.Inventory(Snapshot).Count(e=>Equals(e["nativeTypeCode"],type)&&Equals(e["nativeId"],id))==1,"Unknown fake target.");return Fault=="alias_mapping"?40:Fault=="mapping_after_command"&&Calls>0?999:type;}
   public double[] Bounds(int composite){var result=Boxes[composite].ToArray();if(Fault=="invalid_bounds")result[0]=double.NaN;return result;}
   public RotationSampleVerifier.Samples Measurements(int composite) {
    MeasurementCalls++;
    var entity=EntitySets.Inventory(Snapshot).Single(e=>Equals(e["nativeTypeCode"],composite));var bounds=Boxes[composite].ToArray();
    var positions=Enumerable.Range(0,8).Select(mask=>new RotationSampleVerifier.Witness("fake-box-corner-"+mask,Enumerable.Range(0,3).Select(i=>bounds[2*i+((mask&(1<<i))==0?0:1)]).ToArray())).ToArray();
    if(Fault=="before_measurement_bad"&&MeasurementCalls==1||Fault=="after_measurement_bad"&&MeasurementCalls==3)positions[0].Value[0]=double.NaN;
    if(Fault=="before_measurement_drift"&&MeasurementCalls==2)positions[0].Value[0]+=1;
    return new RotationSampleVerifier.Samples(composite,(int)entity["nativeId"],"world","document_raw",new string('a',64),positions,new RotationSampleVerifier.Witness[0],new double[0],bounds);
   }
   public int Rotate(int composite,int angle,double[] axis) {
    Calls++;Commands.Add(new Dictionary<string,object>{{"compositeId",composite},{"angleDegrees",angle},{"axis",axis.ToArray()}});
    if(Fault=="throw_before_effect"||Fault=="partial_second_throw"&&Calls==2)throw new IOException("Own simulated lost COM return.");
    if(Fault=="return_zero"||Fault=="partial_second_zero"&&Calls==2)return 0;
    if(Fault!="noop_success") {
     // Deliberately arbitrary synthetic effect: executor must never call this geometry verified.
     Boxes[composite]=Boxes[composite].Select((v,index)=>v+index/2+1).ToArray();var target=EntitySets.Inventory(Snapshot).Single(e=>Equals(e["nativeTypeCode"],composite));
     target["geometry"]=new Dictionary<string,object>{{"syntheticEffect",Calls}};
    }
    if(Fault=="untargeted_changed")EntitySets.Inventory(Snapshot).Single(e=>Equals(e["nativeTypeCode"],21))["name"]="Own unexpected change";
    if(Fault=="changed_process_after")Snapshot["processId"]=99;
    if(Fault=="throw_after_effect")throw new IOException("Own simulated effect with lost return.");return 1;
   }
   public void Dispose(){}
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-rotation-execution-fixture")),"Explicit isolated rotation execution marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   string[] faults={"control","native_dto","mixed_targets","noop_success","negative_angle","oblique_axis","throw_before_effect","throw_after_effect","return_zero","partial_second_throw","partial_second_zero","untargeted_changed","changed_process_after","late_context","alias_mapping","invalid_bounds","packet_sha","fenced_copy","busy_executor","previous_target_changed","after_start_context","mapping_after_command","before_measurement_bad","before_measurement_drift","after_measurement_bad"};
   foreach(string fault in faults) {
    bool mixed=new[]{"mixed_targets","partial_second_throw","partial_second_zero","alias_mapping","previous_target_changed"}.Contains(fault);var f=new OwnedRotationPlanTests.Fixture(mixed);
    if(fault=="negative_angle")((Dictionary<string,object>)f.Plan["parameters"])["angleDegrees"]=-90;
    if(fault=="oblique_axis"){((Dictionary<string,object>)f.Plan["parameters"])["direction"]=new[]{1,1,1};double unit=1/Math.Sqrt(3);f.Plan["nativeAxis"]=new[]{10.0,20,30,unit,unit,unit};}
    f.Freeze();string root=f.Base.Root,packetId=Guid.NewGuid().ToString("N");var prepared=OwnedRotationPlans.Prepare(root,packetId,f.Args,f.Base.NewContext());Require(Equals(prepared["state"],"owned_rotation_prepared_observed"),"Own packet preparation failed.");
    var args=new Dictionary<string,object>{{"ownedRotationId",packetId},{"ownedRotationSha256",prepared["ownedRotationSha256"]}};var fake=new Fake(f,fault);FileStream busy=null;
    if(fault=="packet_sha")args["ownedRotationSha256"]=new string('f',64);
    if(fault=="fenced_copy")File.WriteAllText(OwnedTranslation.FencePath(root,(string)prepared["copyId"]),"Own pre-existing fence");
    var before=Hashes(root);string id=Guid.NewGuid().ToString("N");if(fault=="busy_executor")busy=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    Dictionary<string,object> result;try{result=OwnedRotationExecution.Execute(root,id,args,fake);}finally{if(busy!=null)busy.Dispose();}
    bool control=new[]{"control","native_dto","mixed_targets","noop_success","negative_angle","oblique_axis"}.Contains(fault);bool preflight=new[]{"late_context","alias_mapping","invalid_bounds","packet_sha","fenced_copy","busy_executor","before_measurement_bad"}.Contains(fault);
    string expected=control?"owned_rotation_executed_unverified":preflight?"owned_rotation_rejected_before_change":"owned_rotation_outcome_unknown";Require(Equals(result["state"],expected),fault+": "+Program.Json.Serialize(result));
    int expectedCalls=preflight||fault=="after_start_context"||fault=="before_measurement_drift"?0:fault=="previous_target_changed"?1:mixed?(control?3:2):1;Require(fake.Calls==expectedCalls&&Equals(result["commandCallsAttempted"],expectedCalls),"Unexpected command count.");Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Protected original bytes changed.");
    if(control)Require(Equals(result["geometryVerified"],false)&&Equals(result["copyFenced"],true)&&Equals(result["recordedJournalState"],"executed_unverified"),"Unverified batch gained authority.");
    if(!preflight)Require(Equals(result["copyFenced"],true),"Unverified or partial execution lost fence.");
    var after=Hashes(root);var repeated=new Dictionary<string,object>{{"state","not_attempted_before_claim"}};
    if(!preflight||result.ContainsKey("localArtifactsMayExist")||fault=="packet_sha"||fault=="fenced_copy") {var replay=new Fake(f,"control");repeated=OwnedRotationExecution.Execute(root,Guid.NewGuid().ToString("N"),args,replay);Require(Equals(repeated["state"],"owned_rotation_rejected_before_change")&&replay.Calls==0,"Replay executed geometry.");}
    // executor.lock may be created by a prior busy/preflight rejection; no artifacts may be rewritten.
    Require(after.All(pair=>Program.Hash(pair.Key)==pair.Value),"Replay modified existing proof.");
    foreach(var command in fake.Commands){Require(Equals(command["angleDegrees"],((Dictionary<string,object>)f.Plan["parameters"])["angleDegrees"]),"Angle changed.");Require(EntitySets.Canonical(command["axis"])==EntitySets.Canonical(f.Plan["nativeAxis"]),"Explicit axis changed.");}
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"requestId",id},{"parameters",args},{"result",result},{"commands",fake.Commands},{"replayResult",repeated},{"protectedHashes",before},{"artifactHashes",Hashes(root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-rotation-execution-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false},{"productionEntryPointAvailable",false}});
   Console.WriteLine("PASS: "+rows.Count+" internal batch rotation scenarios; fake command callbacks only, no CAD.");return rows.Count;
  }
 }
}
