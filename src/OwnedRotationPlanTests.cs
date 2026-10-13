using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedRotationPlanTests {
  static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal sealed class Fixture {
   internal readonly OwnedOperationPlanTests.Fixture Base=new OwnedOperationPlanTests.Fixture(true,true);
   internal readonly string PlanId=Guid.NewGuid().ToString("N");internal readonly Dictionary<string,object> Plan,Args;
   internal Fixture(bool mixed=false) {
    var entry=(Dictionary<string,object>)((IList)Base.Plan["entitySets"])[0];
    if(mixed) {
     var set=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Base.Root,"entitysets",Base.SetId+".json")));set["entityKeys"]=new[]{new[]{40,5},new[]{21,8},new[]{59,10}};
     string id=Guid.NewGuid().ToString("N"),sha=NativeJournal.Freeze(Path.Combine(Base.Root,"entitysets",id+".json"),set);entry=new Dictionary<string,object>{{"entitySetId",id},{"entitySetSha256",sha},{"entityKeys",set["entityKeys"]}};
    }
    Plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",PlanId},{"operation","rotate_entities"},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",Base.Plan["expiresUtc"]},{"executionEnabled",false},
     {"parameters",new Dictionary<string,object>{{"angleDegrees",90},{"origin",new[]{10,20,30}},{"direction",new[]{0,0,2}},{"coordinateFrame","world"},{"lengthUnits","document_raw"}}},
     {"entitySet",new Dictionary<string,object>{{"entitySetId",entry["entitySetId"]},{"entitySetSha256",entry["entitySetSha256"]}}},{"sourceIdentityDigest",new string('a',64)},{"sourceObservedDigest",new string('b',64)},{"entityKeys",entry["entityKeys"]},
     {"nativeMethod","IPshapeModelDoc.RotateItem"},{"nativeAngleType","long (integer degrees)"},{"nativeAxis",new double[]{10,20,30,0,0,1}},{"lengthUnitsRaw",Base.Snapshot["lengthUnitsRaw"]},{"lengthUnitFactorRaw",Base.Snapshot["lengthUnitFactorRaw"]},
     {"requiredBeforeExecution",new[]{"qualified_native_rotation_executor","owned_working_copy","independent_checkpoint","world_frame_guard","fresh_same_context_guard","independent_postconditions","recovery_fault_matrix"}},{"qualification","Synthetic passive rotation plan; no CAD"}};
    Args=new Dictionary<string,object>{{"rotationPlanId",PlanId},{"rotationPlanSha256",null},{"bindingId",Base.Parameters["bindingId"]},{"bindingSha256",Base.Parameters["bindingSha256"]}};
   }
   internal void Freeze() {
    Directory.CreateDirectory(Path.Combine(Base.Root,"rotationplans"));string sha=NativeJournal.Freeze(Path.Combine(Base.Root,"rotationplans",PlanId+".json"),Plan);Args["rotationPlanSha256"]=sha;
    new NativeJournal(Base.Root,PlanId,sha).Append("prepared",new Dictionary<string,object>{{"executionEnabled",false},{"cadMutationAttempted",false}});
   }
  }
  internal static int Run() {
   Require(File.Exists(Path.Combine(Program.Root,".owned-rotation-fixture")),"Explicit isolated owned rotation marker required; no CAD.");var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","mixed_targets","wrong_axis","fractional_angle","wrong_targets","wrong_native_method","wrong_requirements","expired","plan_sha","fenced_copy","changed_workplane","changed_document","late_context","checkpoint_writable","busy_executor"}) {
    var f=new Fixture(fault=="mixed_targets");string root=f.Base.Root,id=Guid.NewGuid().ToString("N");var context=f.Base.NewContext();FileStream busy=null;bool control=fault=="control"||fault=="mixed_targets";
    if(fault=="wrong_axis")f.Plan["nativeAxis"]=new double[]{10,20,30,1,0,0};
    if(fault=="fractional_angle")((Dictionary<string,object>)f.Plan["parameters"])["angleDegrees"]=0.5;
    if(fault=="wrong_targets")f.Plan["entityKeys"]=new[]{new[]{40,999}};
    if(fault=="wrong_native_method")f.Plan["nativeMethod"]="NeverExecute";
    if(fault=="wrong_requirements")f.Plan["requiredBeforeExecution"]=new string[0];
    if(fault=="expired")f.Plan["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    f.Freeze();
    if(fault=="plan_sha")f.Args["rotationPlanSha256"]=new string('f',64);
    if(fault=="fenced_copy")File.WriteAllText(Path.Combine(Path.GetDirectoryName(f.Base.Working),"mutation-fence.json"),"Own synthetic unresolved mutation.");
    if(fault=="changed_workplane")context.Snapshot["contextAfter"]=new[]{1,0};
    if(fault=="changed_document")((Dictionary<string,object>)context.Snapshot["model"])["id"]=99;
    if(fault=="late_context")context.DuringRead=count=>{if(count==4)context.Changed=true;};
    var owner=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(f.Base.Working),"ownership.json")));string cp=(string)((Dictionary<string,object>)owner["checkpoint"])["path"];
    if(fault=="checkpoint_writable")File.SetAttributes(cp,FileAttributes.Normal);
    var before=Hashes(root);
    if(fault=="busy_executor")busy=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    Dictionary<string,object> result;try{result=OwnedRotationPlans.Prepare(root,id,f.Args,context);}finally{if(busy!=null)busy.Dispose();}
    Require(Equals(result["state"],control?"owned_rotation_prepared_observed":"owned_rotation_preparation_rejected"),fault+": wrong outcome "+Program.Json.Serialize(result));
    Require(before.All(pair=>Program.Hash(pair.Key)==pair.Value),"Original artifacts changed.");
    Require(Equals(result["executionEnabled"],false)&&Equals(result["geometryMutationAttempted"],false)&&Equals(result["contextChangeAttempted"],false)&&Equals(result["cadSaveAttempted"],false),"Unexpected action authority.");
    if(control)Require(context.Reads==4&&Equals(result["guardPasses"],2),"Expected two held guards/four observations.");
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"requestId",id},{"parameters",f.Args},{"result",result},{"protectedHashes",before},{"artifactHashes",Hashes(root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","owned-rotation-plan-tests.json"),new Dictionary<string,object>{{"schema","psbridge.owned-rotation-plan-matrix.v1"},{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false}});
   Console.WriteLine("PASS: "+rows.Count+" native owned rotation preparation cases; fake contexts only, no CAD.");return rows.Count;
  }
 }
}
