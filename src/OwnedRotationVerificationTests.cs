using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using PowerShapeGeometry;

namespace PowerShapeBridge {
 internal static class OwnedRotationVerificationTests {
  static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
  static Dictionary<string,object> Clone(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal static void WaitAfter(DateTime returnedUtc) {
   var timer=Stopwatch.StartNew();while(DateTime.UtcNow<=returnedUtc){Require(timer.Elapsed<TimeSpan.FromSeconds(1),"Fixture clock did not advance past actual command return.");Thread.Sleep(1);}
  }
  internal sealed class Context : OwnedRotationExecution.IContext,OwnedRotationVerification.IContext {
   internal Dictionary<string,object> Snapshot,Listing;internal readonly Dictionary<int,RotationSampleVerifier.Samples> Data=new Dictionary<int,RotationSampleVerifier.Samples>();internal int Calls,Reads;internal string Fault;
   internal Context(OwnedRotationPlanTests.Fixture f){Snapshot=Clone(f.Base.Snapshot);Listing=Clone(f.Base.Listing);foreach(var entity in EntitySets.Inventory(Snapshot)){int type=(int)entity["nativeTypeCode"];double[] bounds={-10,10,-10,10,0,20};var positions=Enumerable.Range(0,8).Select(mask=>new RotationSampleVerifier.Witness("anchor-"+mask,Enumerable.Range(0,3).Select(i=>bounds[2*i+((mask&(1<<i))==0?0:1)]).ToArray())).ToArray();Data[type]=new RotationSampleVerifier.Samples(type,(int)entity["nativeId"],"world","document_raw",new string('a',64),positions,new[]{new RotationSampleVerifier.Witness("direction-x",new double[]{1,0,0})},new double[]{1},bounds);}}
   public Dictionary<string,object> Observation(){Reads++;if(Fault=="late_context"&&Reads==4)Snapshot["processId"]=99;return Clone(Snapshot);}
   public Dictionary<string,object> Windows(){return Clone(Listing);}
   public void ConfirmDocument(){}
   public int Composite(int type,int id){Require(Data.ContainsKey(type)&&Data[type].NativeId==id,"Native key absent.");return Fault=="wrong_mapping"?999:type;}
   public double[] Bounds(int composite){return Data[composite].BoundsPaired.ToArray();}
   public RotationSampleVerifier.Samples Measurements(int composite){var source=Data[composite];var result=RotationMeasurementRecords.Decode(Clone(RotationMeasurementRecords.Encode(source)));if(Fault=="wrong_samples")result.Positions[0].Value[0]+=1;return result;}
   static double[] Quarter(double[] point,int angle,double[] axis,bool direction=false){Require(angle==90||angle==-90,"Analytic quarter-turn fixture only.");double x=point[0]-(direction?0:axis[0]),y=point[1]-(direction?0:axis[1]);return new[]{(direction?0:axis[0])+(angle==90?-y:y),(direction?0:axis[1])+(angle==90?x:-x),point[2]};}
   public int Rotate(int composite,int angle,double[] axis) {
    Calls++;if(Fault=="partial_second"&&Calls==2)return 0;
    var source=Data[composite];if(Fault=="wrong_angle")angle=-angle;
    var points=source.Positions.Select(p=>new RotationSampleVerifier.Witness(p.Key,Quarter(p.Value,angle,axis))).ToArray();var directions=source.Directions.Select(p=>new RotationSampleVerifier.Witness(p.Key,Quarter(p.Value,angle,axis,true))).ToArray();var bounds=Enumerable.Range(0,3).SelectMany(i=>new[]{points.Min(p=>p.Value[i]),points.Max(p=>p.Value[i])}).ToArray();
    Data[composite]=new RotationSampleVerifier.Samples(source.Type,source.NativeId,source.Frame,source.Units,source.DescriptorSha256,points,directions,source.Invariants,bounds);var entity=EntitySets.Inventory(Snapshot).Single(e=>Equals(e["nativeTypeCode"],composite));if(composite==40)((Dictionary<string,object>)entity["geometry"])["cogRaw"]=Quarter(new double[]{0,0,10},angle,axis);else entity["geometry"]=new Dictionary<string,object>{{"syntheticRotation",true}};return 1;
   }
   internal Context Fresh(OwnedRotationPlanTests.Fixture f){var result=new Context(f){Snapshot=Clone(Snapshot),Listing=Clone(Listing)};foreach(var pair in Data)result.Data[pair.Key]=RotationMeasurementRecords.Decode(Clone(RotationMeasurementRecords.Encode(pair.Value)));return result;}
   public void Dispose(){}
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".rotation-verification-fixture")),"Explicit own verifier marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","mixed_targets","negative_angle","wrong_angle","wrong_samples","wrong_mapping","late_context","wrong_receipt_sha","wrong_before_sha","wrong_summary_sha","changed_fence","partial_second","old_inspection","equal_inspection","wrong_window"}) {
    var f=new OwnedRotationPlanTests.Fixture(fault=="mixed_targets"||fault=="partial_second");if(fault=="negative_angle")((Dictionary<string,object>)f.Plan["parameters"])["angleDegrees"]=-90;f.Freeze();string root=f.Base.Root,packetId=Guid.NewGuid().ToString("N");var prepared=OwnedRotationPlans.Prepare(root,packetId,f.Args,f.Base.NewContext());
    var executionContext=new Context(f){Fault=fault=="wrong_angle"||fault=="partial_second"?fault:null};string id=Guid.NewGuid().ToString("N");var executed=OwnedRotationExecution.Execute(root,id,new Dictionary<string,object>{{"ownedRotationId",packetId},{"ownedRotationSha256",prepared["ownedRotationSha256"]}},executionContext);Require(Equals(executed["state"],fault=="partial_second"?"owned_rotation_outcome_unknown":"owned_rotation_executed_unverified"),"Own analytic execution failed: "+Program.Json.Serialize(executed));
    string receiptSha=NativeJournal.Freeze(Path.Combine(root,"responses",id+".json"),executed);var fresh=executionContext.Fresh(f);fresh.Fault=new[]{"wrong_samples","wrong_mapping","late_context"}.Contains(fault)?fault:null;
    var returnedEvent=Directory.GetFiles(Path.Combine(root,"operations",id),"*.json").Select(path=>Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path))).SingleOrDefault(e=>Equals(e["state"],"command_returned"));
    WaitAfter(EntitySets.Expiry(returnedEvent==null?executed["timestampUtc"]:returnedEvent["timestampUtc"]));
    var inspection=Clone(fresh.Snapshot);inspection["timestampUtc"]=fault=="old_inspection"?f.Base.Snapshot["timestampUtc"]:fault=="equal_inspection"?returnedEvent["timestampUtc"]:DateTime.UtcNow.ToString("o");string inspectionId=Guid.NewGuid().ToString("N");inspection["bridgeRequestId"]=inspectionId;string inspectionSha=NativeJournal.Freeze(Path.Combine(root,"responses",inspectionId+".json"),inspection);
    var listing=Clone(fresh.Listing);listing["timestampUtc"]=DateTime.UtcNow.ToString("o");listing["state"]="documents_listed_verified";string listingId=Guid.NewGuid().ToString("N"),listingSha=NativeJournal.Freeze(Path.Combine(root,"responses",listingId+".json"),listing);
    var args=new Dictionary<string,object>{{"executionRequestId",id},{"executionReceiptSha256",receiptSha},{"inspectionRequestId",inspectionId},{"inspectionSha256",inspectionSha},{"listingRequestId",listingId},{"listingSha256",listingSha},{"windowNumber",fault=="wrong_window"?99:1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(180).ToString("o")}};
    if(fault=="wrong_receipt_sha")args["executionReceiptSha256"]=new string('f',64);
    if(fault=="wrong_before_sha")File.AppendAllText(RotationMeasurementRecords.PathFor(root,id,0,"before")," ");
    if(fault=="wrong_summary_sha")File.AppendAllText(Path.Combine(root,"rotationcommands",id,"summary.json")," ");
    if(fault=="changed_fence")File.AppendAllText(OwnedTranslation.FencePath(root,(string)executed["copyId"])," ");
    var before=Hashes(root);string verificationId=Guid.NewGuid().ToString("N");var result=OwnedRotationVerification.Verify(root,verificationId,args,fresh);bool control=new[]{"control","mixed_targets","negative_angle"}.Contains(fault);
    Require(Equals(result["state"],control?"observed_rotation_samples_match_unqualified":"owned_rotation_verification_rejected"),fault+": "+Program.Json.Serialize(result));Require(fresh.Calls==0&&executionContext.Calls==(fault=="mixed_targets"?3:fault=="partial_second"?2:1),"Verification repeated geometry.");Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Verifier changed original proof/fence.");
    Require(Equals(result["copyFenceReleased"],false)&&Equals(result["nativeConventionQualified"],false)&&File.Exists(OwnedTranslation.FencePath(root,(string)executed["copyId"])),"Unqualified verifier released fence.");
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"executionId",id},{"verificationId",verificationId},{"parameters",args},{"result",result},{"executionCalls",executionContext.Calls},{"verificationCalls",fresh.Calls},{"protectedHashes",before},{"artifactHashes",Hashes(root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","rotation-verification-tests.json"),new Dictionary<string,object>{{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false},{"fenceAuthorityAvailable",false}});Console.WriteLine("PASS: "+rows.Count+" independent rotation sample verification scenarios; no CAD/fence release.");return rows.Count;
  }
 }
}
