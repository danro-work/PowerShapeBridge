using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Production wiring for the narrow, live-qualified case. Existing executor owns
 // the one-use claim/fence; independent verification never invokes RotateItem.
 internal static class OwnedSolidQuarterTurn {
  internal const string Scope="one_solid_world_z_plus_90_mm_observed_samples";
  internal const string PivotedScope="one_solid_world_z_signed_quarter_turn_mm_observed_samples";
  internal static bool Supported(int angle,double[] axis){return (angle==90||angle==-90)&&axis!=null&&axis.Length==6&&axis.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)&&Math.Abs(v)<=1000000)&&axis.Skip(3).SequenceEqual(new double[]{0,0,1});}
  internal static string ScopeFor(int angle,double[] axis){Need(Supported(angle,axis),"Only signed WORLD Z quarter turns with explicit bounded pivot are supported.");return angle==90&&axis.Take(3).All(v=>v==0)?Scope:PivotedScope;}
  internal static readonly string[] Recipes={"prepare_owned_solid_quarter_turn","execute_owned_solid_quarter_turn","verify_owned_solid_quarter_turn"};
  static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
  static string PathFor(string root,string folder,object id){Need(EntitySets.Hex(id,32),"Fixed quarter-turn proof identity required.");string path=Path.Combine(root,folder,(string)id+".json");OwnedCopies.NoReparse(path);return path;}
  static Dictionary<string,object> Object(Dictionary<string,object> d,string key){var value=d[key] as Dictionary<string,object>;Need(value!=null,"Object proof required: "+key);return value;}
  internal static void CheckScope(Dictionary<string,object> plan,Dictionary<string,object> expected){
   var p=Object(plan,"parameters");var keys=EntitySets.Keys(plan["entityKeys"]);
   Need((Equals(p["angleDegrees"],90)||Equals(p["angleDegrees"],-90))&&Supported((int)p["angleDegrees"],OwnedRotationPlans.Axis(p))&&keys.Length==1&&keys[0][0]==40&&EntitySets.Inventory(expected).Length==1&&Equals(expected["lengthUnitsRaw"],"mm")&&Program.StrictDouble(expected["lengthUnitFactorRaw"])==1,"Only one SOLID +/-90 WORLD Z with explicit pivot in mm/factor1 is supported.");
  }
  internal static void Validate(string recipe,Dictionary<string,object> args){
   Need(Recipes.Contains(recipe),"Unknown solid quarter-turn recipe.");
   if(recipe==Recipes[0]){OwnedRotationPlans.Validate(args);return;}
   if(recipe==Recipes[2]){OwnedRotationVerification.Validate(args);return;}
   string[] fields={"ownedRotationId","ownedRotationSha256","preparationReceiptSha256","solidWitnessBundleSha256"};
   Need(args.Count==fields.Length&&fields.All(k=>args.ContainsKey(k)&&EntitySets.Hex(args[k],k=="ownedRotationId"?32:64)),"Exact quarter-turn preparation proofs required.");
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args,SolidFaceMeasurements.IContext context){
   // Check the bounded scope before creating a prepared packet or descriptor.
   using(var proofs=new OwnedOperationPlans.Proofs()){var d=new OwnedRotationPlans.Dependencies(root,args,proofs);CheckScope(d.Plan,d.Expected);}
   var result=OwnedRotationPlans.Prepare(root,id,args,context);
   if(!Equals(result["state"],"owned_rotation_prepared_observed"))return result;
   try{
    using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))using(var proofs=new OwnedOperationPlans.Proofs()){
     var d=new OwnedRotationPlans.Dependencies(root,args,proofs);CheckScope(d.Plan,d.Expected);var packet=Object(result,"ownedRotation");
     using(var owner=OwnedCopies.Hold(root,(string)packet["copyId"],(string)packet["ownershipSha256"])){
      Need(!File.Exists(OwnedTranslation.FencePath(root,(string)packet["copyId"])),"Prepared copy became fenced.");d.Check(owner,context,result);
      var descriptor=SolidFaceMeasurements.Capture(d.Expected,d.Keys[0][1],context);
      // Force a complete readable before-sample set, with no COG-only fallback.
      SolidFaceMeasurements.Read(descriptor,context);d.Check(owner,context,result);owner.Recheck();
      var bundle=new Dictionary<string,object>{{"schemaVersion",1},{"ownedRotationId",id},{"ownedRotationSha256",result["ownedRotationSha256"]},{"bindingId",packet["bindingId"]},{"bindingSha256",packet["bindingSha256"]},{"copyId",packet["copyId"]},{"ownershipSha256",packet["ownershipSha256"]},{"windowContextSha256",packet["windowContextSha256"]},{"descriptor",descriptor}};
      string path=PathFor(root,"solidrotationdescriptors",id);Directory.CreateDirectory(Path.GetDirectoryName(path));result["solidWitnessBundleSha256"]=NativeJournal.Freeze(path,bundle);var parameters=Object(d.Plan,"parameters");result["qualificationScope"]=ScopeFor((int)parameters["angleDegrees"],OwnedRotationPlans.Axis(parameters));
     }
    }
   }catch(Exception error){result["state"]="owned_solid_quarter_turn_preparation_rejected";result["error"]=error.Message;result["retryAllowed"]=false;}
   return result;
  }
  internal static Dictionary<string,object> LoadBundle(string root,Dictionary<string,object> references,OwnedOperationPlans.Proofs proofs,out Dictionary<string,object> packet,out Dictionary<string,object> expected,out string scope){
   string id=(string)references["ownedRotationId"];
   var prepared=proofs.Json(PathFor(root,"responses",id),(string)references["preparationReceiptSha256"],16000000);
   Need(Equals(prepared["state"],"owned_rotation_prepared_observed")&&Equals(prepared["ownedRotationId"],id)&&Equals(prepared["ownedRotationSha256"],references["ownedRotationSha256"])&&Equals(prepared["solidWitnessBundleSha256"],references["solidWitnessBundleSha256"]),"Original qualified preparation receipt differs.");
   packet=proofs.Json(PathFor(root,"ownedrotations",id),(string)references["ownedRotationSha256"]);
   Need(EntitySets.Canonical(packet)==EntitySets.Canonical(prepared["ownedRotation"]),"Prepared packet differs from its receipt.");
   var bundle=proofs.Json(PathFor(root,"solidrotationdescriptors",id),(string)references["solidWitnessBundleSha256"],16000000);
   Need(Equals(bundle["ownedRotationId"],id)&&Equals(bundle["ownedRotationSha256"],references["ownedRotationSha256"]),"Descriptor bundle rotation identity differs.");
   foreach(string key in new[]{"bindingId","bindingSha256","copyId","ownershipSha256","windowContextSha256"})Need(Equals(bundle[key],packet[key]),"Descriptor owner/binding differs: "+key);
   var binding=proofs.Json(PathFor(root,"documentbindings",packet["bindingId"]),(string)packet["bindingSha256"]);
   expected=proofs.Json(PathFor(root,"responses",binding["inspectionRequestId"]),(string)binding["inspectionSha256"],16000000);
   var plan=proofs.Json(PathFor(root,"rotationplans",packet["rotationPlanId"]),(string)packet["rotationPlanSha256"]);CheckScope(plan,expected);
   var parameters=Object(plan,"parameters");scope=ScopeFor((int)parameters["angleDegrees"],OwnedRotationPlans.Axis(parameters));Need(Equals(prepared["qualificationScope"],scope),"Preparation scope differs from its pinned parameters.");
   Need(EntitySets.Canonical(Object(bundle,"descriptor")["entityKey"])==EntitySets.Canonical(EntitySets.Keys(plan["entityKeys"])[0]),"Descriptor targets another entity.");return bundle;
  }
  internal static Dictionary<string,object> Run(string id,string recipe,Dictionary<string,object> args){
   var config=Program.ReadConfig();Need(config.IsGeneralCore&&config.ownedSolidQuarterTurnsExperimental,"Solid quarter-turn execution disabled.");Validate(recipe,args);Directory.CreateDirectory(Path.Combine(Program.Root,"operations"));
   if(recipe==Recipes[0])using(var context=new DocumentBindings.ComContext(config))return Prepare(Program.Root,id,args,context);
   using(var proofs=new OwnedOperationPlans.Proofs()){
    var references=new Dictionary<string,object>(args);
    if(recipe==Recipes[2]){
     var receipt=proofs.Json(PathFor(Program.Root,"responses",args["executionRequestId"]),(string)args["executionReceiptSha256"],16000000);var plan=Object(receipt,"plan");
     references=new Dictionary<string,object>{{"ownedRotationId",plan["ownedRotationId"]},{"ownedRotationSha256",plan["ownedRotationSha256"]},{"preparationReceiptSha256",receipt["preparationReceiptSha256"]},{"solidWitnessBundleSha256",receipt["solidWitnessBundleSha256"]}};
    }
    Dictionary<string,object> packet,expected;string scope;var bundle=LoadBundle(Program.Root,references,proofs,out packet,out expected,out scope);
    using(var context=new DocumentBindings.ComContext(config)){
     Dictionary<string,object> result;
     if(recipe==Recipes[1])using(var adapter=new SolidRotationContext.ExecutionContext(context,expected,Object(bundle,"descriptor"),(string)packet["windowContextSha256"]))result=OwnedRotationExecution.Execute(Program.Root,id,new Dictionary<string,object>{{"ownedRotationId",references["ownedRotationId"]},{"ownedRotationSha256",references["ownedRotationSha256"]}},adapter);
     else using(var adapter=new SolidRotationContext.ReadContext(context,expected,Object(bundle,"descriptor"),(string)packet["windowContextSha256"]))result=OwnedRotationVerification.VerifyQualifiedSolidQuarterTurn(Program.Root,id,args,adapter);
     result["preparationReceiptSha256"]=references["preparationReceiptSha256"];result["solidWitnessBundleSha256"]=references["solidWitnessBundleSha256"];result["qualificationScope"]=scope;return result;
    }
   }
  }
 }
}
