using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Local qualification of a trusted staged Native adapter, before any new scale.
// Loads getter methods only. Never invokes worker Main, a command or fence cleanup.
internal static class ReadNativeScaleSource {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static readonly BindingFlags Static=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 static void Need(bool value,string why){if(!value)throw new InvalidDataException(why);}
 static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,object> Graph(object o){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(o));}
 static object Get(object context,string method,params object[] args){Need(new[]{"Observation","Windows","ConfirmDocument","Composite","Bounds"}.Contains(method),"Getter not allowed.");return context.GetType().GetMethod(method).Invoke(context,args);}
 [STAThread]static int Main(string[] args){
  if(args.Length!=5||File.Exists(args[4]))return 2;
  object context=null;int exit=1;var report=new Dictionary<string,object>{{"state","native_scale_source_measurement_rejected"},{"cadCommands",0},{"selectionChanges",0},{"cadSaves",0},{"fenceReleased",false},{"workerStarted",false},{"executionAuthority",false},{"publicScaleRouteQualified",false}};
  try{
   Need(Hash(args[0])==args[1]&&Hash(args[2])==args[3],"Trusted staged assembly/readiness spec changed.");
   var spec=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));string config=(string)spec["configurationPath"],configSha=(string)spec["configurationSha256"];
   Need(Hash(config)==configSha&&Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.config.json"))==configSha,"Exact runtime and diagnostic configuration required.");
   var protectedFiles=((System.Collections.IEnumerable)spec["protectedFiles"]).Cast<object>().Select(Graph).ToArray();
   Action pins=()=>{foreach(var file in protectedFiles)Need(Hash((string)file["path"])==(string)file["sha256"],"Protected source/checkpoint/working/fence changed.");};pins();
   var native=Assembly.LoadFrom(Path.GetFullPath(args[0]));var program=native.GetType("PowerShapeBridge.Program",true);var parse=program.GetMethod("ParseConfig",Static);var parsed=parse.Invoke(null,new object[]{File.ReadAllText(config)});
   context=Activator.CreateInstance(native.GetType("PowerShapeBridge.DocumentBindings+ComContext",true),BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{parsed},null);
   var start=Graph(Get(context,"Observation"));var model=Graph(start["model"]);Need(string.Equals(Path.GetFullPath((string)model["path"]),Path.GetFullPath((string)spec["workingPath"]),StringComparison.OrdinalIgnoreCase),"Fixed owned research copy must be active.");
   var owner=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText((string)spec["ownershipPath"]));Need(string.Equals(Path.GetFullPath((string)Graph(owner["working"])["path"]),Path.GetFullPath((string)spec["workingPath"]),StringComparison.OrdinalIgnoreCase),"Fixed owner working path differs.");
   var bindings=native.GetType("PowerShapeBridge.DocumentBindings",true);var digest=bindings.GetMethod("WindowDigest",Static);var windows=Get(context,"Windows");string windowSha=(string)digest.Invoke(null,new[]{windows});Need(windowSha==(string)spec["windowContextSha256"],"Recorded window context changed.");
   var saved=native.GetType("PowerShapeBridge.WorkingCopyOpen",true).GetMethod("SavedHashes",Static);var savedBefore=saved.Invoke(null,new[]{windows});
   var adapter=native.GetType("PowerShapeBridge.SolidScaleMembershipMeasurements",true);var bundle=adapter.GetMethod("CaptureSource",Static).Invoke(null,new object[]{start,Convert.ToInt32(spec["nativeId"]),windowSha,context});
   var before=adapter.GetMethod("Source",Static).Invoke(null,new object[]{bundle,start});var witnesses=(System.Collections.IEnumerable)before.GetType().GetField("Witnesses",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(before);
   Need(witnesses.Cast<object>().Count()==8,"Eight captured source XYZ required.");var finish=Graph(Get(context,"Observation"));var observed=native.GetType("PowerShapeBridge.EntitySets",true).GetMethod("ObservedDigest",Static);Need(Equals(observed.Invoke(null,new object[]{start}),observed.Invoke(null,new object[]{finish})),"Source geometry changed during capture.");
   var endWindows=Get(context,"Windows");Need((string)digest.Invoke(null,new[]{endWindows})==windowSha&&Json.Serialize(savedBefore)==Json.Serialize(saved.Invoke(null,new[]{endWindows})),"Windows/saved model bytes changed during capture.");pins();Get(context,"ConfirmDocument");
   report["state"]="native_scale_source_xyz_captured_observed";report["sourceBundle"]=bundle;report["beforeObservation"]=start;report["afterObservation"]=finish;report["savedHashes"]=savedBefore;report["nativeAssemblySha256"]=args[1];report["scenarioSha256"]=args[3];report["protectedFilesVerified"]=protectedFiles.Length;exit=0;
  }catch(Exception error){report["error"]=(error.InnerException??error).ToString();}
  finally{if(context!=null)((IDisposable)context).Dispose();report["timestampUtc"]=DateTime.UtcNow.ToString("o");using(var f=new FileStream(args[4],FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var w=new StreamWriter(f))w.Write(Json.Serialize(report));}
  Console.WriteLine(Json.Serialize(new{state=report["state"],error=report.ContainsKey("error")?report["error"]:null}));return exit;
 }
}
