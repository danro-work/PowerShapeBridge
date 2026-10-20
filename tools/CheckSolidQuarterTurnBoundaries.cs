using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

// Offline boundary checks only: no context construction, COM, worker loop or CAD.
class CheckSolidQuarterTurnBoundaries {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer();static Assembly Native;static int Count;
 static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);Count++;}
 static Dictionary<string,object> O(string json){return Json.Deserialize<Dictionary<string,object>>(json);}
 static object Call(string type,string method,params object[] args){return Native.GetType("PowerShapeBridge."+type,true).GetMethod(method,BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,args);}
 static void Reject(Action action){try{action();}catch(TargetInvocationException e){Need(e.InnerException is InvalidDataException||e.InnerException is ArgumentException||e.InnerException is InvalidOperationException||e.InnerException is FormatException,"Unexpected refusal type");return;}throw new Exception("Boundary accepted forbidden input");}
 static int Main(string[] args){try{
  if(args.Length!=1||!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,".solid-quarter-turn-boundary-fixture")))throw new Exception("Own offline fixture marker required");Native=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  Func<Dictionary<string,object>> plan=()=>O("{\"parameters\":{\"angleDegrees\":90,\"origin\":[0,0,0],\"direction\":[0,0,1],\"coordinateFrame\":\"world\",\"lengthUnits\":\"document_raw\"},\"entityKeys\":[[40,5]]}");
  Func<Dictionary<string,object>> observation=()=>O("{\"state\":\"succeeded_verified\",\"modelScope\":\"active_document_read_only\",\"entityCount\":1,\"lengthUnitsRaw\":\"mm\",\"lengthUnitFactorRaw\":1,\"entities\":[{\"nativeTypeCode\":40,\"nativeId\":5}]}");
  Call("OwnedSolidQuarterTurn","CheckScope",plan(),observation());Need(true,"Bounded positive scope");
  foreach(string fault in new[]{"angle","axis","origin_limit","curve","batch","inventory","units","factor"}){var p=plan();var o=observation();var a=(Dictionary<string,object>)p["parameters"];if(fault=="angle")a["angleDegrees"]=45;if(fault=="axis")a["direction"]=new[]{1,1,0};if(fault=="origin_limit")a["origin"]=new[]{1000001,0,0};if(fault=="curve")p["entityKeys"]=new[]{new[]{59,5}};if(fault=="batch")p["entityKeys"]=new[]{new[]{40,5},new[]{40,6}};if(fault=="inventory")o["entities"]=new[]{new Dictionary<string,object>{{"nativeTypeCode",40},{"nativeId",5}},new Dictionary<string,object>{{"nativeTypeCode",40},{"nativeId",6}}};if(fault=="inventory")o["entityCount"]=2;if(fault=="units")o["lengthUnitsRaw"]="inch";if(fault=="factor")o["lengthUnitFactorRaw"]=25.4;Reject(()=>Call("OwnedSolidQuarterTurn","CheckScope",p,o));}
  foreach(int angle in new[]{90,-90})foreach(int[] direction in new[]{new[]{1,0,0},new[]{-1,0,0},new[]{0,1,0},new[]{0,-1,0},new[]{0,0,1},new[]{0,0,-1}}){var p=plan();var a=(Dictionary<string,object>)p["parameters"];a["angleDegrees"]=angle;a["origin"]=new[]{2,3,4};a["direction"]=direction;Call("OwnedSolidQuarterTurn","CheckScope",p,observation());Need(true,"Signed principal scope");string scope=(string)Call("OwnedSolidQuarterTurn","ScopeFor",angle,new double[]{2,3,4}.Concat(direction.Select(v=>(double)v)).ToArray());Need(scope==(direction.SequenceEqual(new[]{0,0,1})?"one_solid_world_z_signed_quarter_turn_mm_observed_samples":"one_solid_world_principal_signed_quarter_turn_mm_observed_samples"),"Scope follows pinned axis");}
  Need((string)Call("OwnedSolidQuarterTurn","ScopeFor",90,new double[]{0,0,0,0,0,1})=="one_solid_world_z_plus_90_mm_observed_samples","Legacy origin scope retained");
  var execute=new Dictionary<string,object>{{"ownedRotationId",new string('a',32)},{"ownedRotationSha256",new string('b',64)},{"preparationReceiptSha256",new string('c',64)},{"solidWitnessBundleSha256",new string('d',64)}};
  Call("OwnedSolidQuarterTurn","Validate","execute_owned_solid_quarter_turn",execute);Need(true,"Pinned execution schema");
  foreach(string field in execute.Keys.ToArray()){var bad=new Dictionary<string,object>(execute);bad.Remove(field);Reject(()=>Call("OwnedSolidQuarterTurn","Validate","execute_owned_solid_quarter_turn",bad));}
  var extra=new Dictionary<string,object>(execute){{"command","NeverRun"}};Reject(()=>Call("OwnedSolidQuarterTurn","Validate","execute_owned_solid_quarter_turn",extra));
  string id=new string('a',32);var envelope=new Dictionary<string,object>{{"requestId",id},{"operation","recipe.prepare"},{"arguments",new Dictionary<string,object>{{"recipeId","execute_owned_solid_quarter_turn"},{"parameters",execute}}}};Call("FileRequests","Parse",id,Json.Serialize(envelope));Need(true,"Configured fixed native route");
  var config=Call("Program","ReadConfig");var flag=config.GetType().GetProperty("ownedRotationPlansExperimental");flag.SetValue(config,false,null);Reject(()=>{config.GetType().GetMethod("Validate").Invoke(config,null);});
  Console.WriteLine("PASS: "+Count+" native quarter-turn scope/envelope boundaries; no CAD or worker loop.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
