using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

// Runs the staging provider as a passive library, never the worker Main.
class ReadSavedPinnedSolidFaces {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 [STAThread] static int Main(string[] args){
  if(args.Length!=4||File.Exists(args[2]))return 2;
  object c=null;var result=new Dictionary<string,object>{{"state","failed_pinned_solid_read"},{"cadMutationAttempted",false},{"workerStarted",false}};int exit=1;
  try{
   var a=Assembly.LoadFrom(Path.GetFullPath(args[0]));var config=a.GetType("PowerShapeBridge.Program").GetMethod("ReadConfig").Invoke(null,null);
   var type=a.GetType("PowerShapeBridge.DocumentBindings+ComContext");var provider=a.GetType("PowerShapeBridge.SolidFaceMeasurements");var records=a.GetType("PowerShapeBridge.RotationMeasurementRecords");
   Func<object> create=()=>Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{config},null);
   Func<object,Dictionary<string,object>> observe=context=>Json.Deserialize<Dictionary<string,object>>(Json.Serialize(type.GetMethod("Observation").Invoke(context,null)));
   Func<object,object> encode=samples=>records.GetMethod("Encode",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{samples});
   c=create();var before=observe(c);result["before"]=before;var model=(Dictionary<string,object>)before["model"];
   Need(string.Equals(Path.GetFullPath((string)model["path"]),Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase),"Expected owned document must be active.");
   var bodies=((System.Collections.IEnumerable)before["entities"]).Cast<Dictionary<string,object>>().Where(e=>Equals(e["nativeTypeCode"],40)).ToArray();Need(bodies.Length==1,"One SOLID required.");
   var bundle=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[3]));var descriptor=(Dictionary<string,object>)bundle["descriptor"];result["descriptor"]=descriptor;
   var read=provider.GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);result["firstSamples"]=encode(read.Invoke(null,new[]{descriptor,c}));
   ((IDisposable)c).Dispose();c=create();result["independentSamples"]=encode(read.Invoke(null,new[]{descriptor,c}));
   Need(Json.Serialize(result["firstSamples"])==Json.Serialize(result["independentSamples"]),"Fresh-context pinned samples differ.");result["after"]=observe(c);
   foreach(string key in new[]{"processId","processStartedUtc","windowsSessionId","model","entities","contextBefore","contextAfter","selectionBefore","selectionAfter","lengthUnitsRaw","lengthUnitFactorRaw"})Need(Json.Serialize(before[key])==Json.Serialize(((Dictionary<string,object>)result["after"])[key]),"Observation changed: "+key);
   result["state"]="pinned_solid_face_provider_live_passed";exit=0;
  }catch(Exception error){result["error"]=(error.InnerException??error).ToString();}
  finally{if(c!=null)((IDisposable)c).Dispose();}
  result["timestampUtc"]=DateTime.UtcNow.ToString("o");
  using(var f=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f))w.Write(Json.Serialize(result));
  Console.WriteLine(Json.Serialize(new{state=result["state"],error=result.ContainsKey("error")?result["error"]:null}));return exit;
 }
}
