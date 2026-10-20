using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Passive staged-library qualification. Never runs worker Main or any command,
// verifier, activation, selection, save, screenshot or fence cleanup.
class ReadGuardedRotationSnapshots {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 static string Sha(string p){using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,object> Copy(object o){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(o));}
 [STAThread] static int Main(string[] args){
  if(args.Length!=5||File.Exists(args[4]))return 2;
  object c=null,reader=null;int exit=1;
  var result=new Dictionary<string,object>{{"state","passive_guarded_snapshot_read_failed"},{"cadMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"workerStarted",false}};
  try{
   Need(Sha(args[2])==args[3],"Pinned descriptor bundle SHA differs.");
   var a=Assembly.LoadFrom(Path.GetFullPath(args[0]));var program=a.GetType("PowerShapeBridge.Program",true);
   result["nativeVersion"]=program.GetField("ReleaseVersion").GetRawConstantValue();result["nativeAssemblySha256"]=Sha(args[0]);
   var config=program.GetMethod("ReadConfig",Static).Invoke(null,null);var type=a.GetType("PowerShapeBridge.DocumentBindings+ComContext",true);
   c=Activator.CreateInstance(type,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{config},null);
   var before=Copy(type.GetMethod("Observation").Invoke(c,null));result["before"]=before;var model=(Dictionary<string,object>)before["model"];
   Need(string.Equals(Path.GetFullPath((string)model["path"]),Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase),"Expected owned document must be active.");
   var bundle=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));var descriptor=(Dictionary<string,object>)bundle["descriptor"];
   var windows=Copy(type.GetMethod("Windows").Invoke(c,null));var bindings=a.GetType("PowerShapeBridge.DocumentBindings",true);var digest=bindings.GetMethod("WindowDigest",Static);
   var entities=a.GetType("PowerShapeBridge.EntitySets",true);var observedDigest=entities.GetMethod("ObservedDigest",Static);
   string windowSha=(string)digest.Invoke(null,new object[]{windows}),observationSha=(string)observedDigest.Invoke(null,new object[]{before});
   var adapter=a.GetType("PowerShapeBridge.SolidRotationContext+ReadContext",true);
   reader=Activator.CreateInstance(adapter,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{c,before,descriptor,windowSha},null);
   Need(adapter.GetMethods().All(m=>m.Name!="Rotate"),"Read adapter unexpectedly exposes rotation.");
   var timings=new List<object>();
   foreach(string method in new[]{"Observation","Windows","Observation","Windows"}){
    var timer=Stopwatch.StartNew();var value=Copy(adapter.GetMethod(method).Invoke(reader,null));timer.Stop();
    string got=(string)(method=="Observation"?observedDigest:digest).Invoke(null,new object[]{value});
    Need(got==(method=="Observation"?observationSha:windowSha),"Fresh guarded snapshot differs: "+method);
    timings.Add(new{method,elapsedMilliseconds=timer.Elapsed.TotalMilliseconds});
   }
   result["timings"]=timings;var key=((System.Collections.IEnumerable)descriptor["entityKey"]).Cast<object>().Select(Convert.ToInt32).ToArray();
   int composite=Convert.ToInt32(adapter.GetMethod("Composite").Invoke(reader,new object[]{key[0],key[1]}));
   var samples=adapter.GetMethod("Measurements").Invoke(reader,new object[]{composite});
   result["samples"]=a.GetType("PowerShapeBridge.RotationMeasurementRecords",true).GetMethod("Encode",Static).Invoke(null,new[]{samples});
   var after=Copy(type.GetMethod("Observation").Invoke(c,null));result["after"]=after;
   Need((string)observedDigest.Invoke(null,new object[]{after})==observationSha,"Observed model changed during passive read.");
   Need((string)digest.Invoke(null,new[]{type.GetMethod("Windows").Invoke(c,null)})==windowSha,"Window context changed during passive read.");
   result["descriptorBundleSha256"]=args[3];result["windowContextSha256"]=windowSha;result["observedDigest"]=observationSha;result["state"]="passive_guarded_rotation_snapshots_verified";exit=0;
  }catch(Exception error){result["error"]=(error.InnerException??error).ToString();}
  finally{if(reader!=null)((IDisposable)reader).Dispose();if(c!=null)((IDisposable)c).Dispose();}
  result["timestampUtc"]=DateTime.UtcNow.ToString("o");
  using(var f=new FileStream(args[4],FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f))w.Write(Json.Serialize(result));
  Console.WriteLine(Json.Serialize(new{state=result["state"],error=result.ContainsKey("error")?result["error"]:null}));return exit;
 }
}
