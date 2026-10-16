using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Passive observer only: no worker loop, queue, selection or context setter.
class ReadOwnedWorkplaneFrames {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static double[] Values(object raw){Need(raw is IEnumerable&&!(raw is string),"Numeric frame required.");var v=((IEnumerable)raw).Cast<object>().Select(Convert.ToDouble).ToArray();Need(v.Length==9&&v.All(x=>!double.IsNaN(x)&&!double.IsInfinity(x)),"Nine finite frame values required.");Need(Math.Abs(v.Skip(3).Take(3).Sum(x=>x*x)-1)<=1e-9&&Math.Abs(v.Skip(6).Take(3).Sum(x=>x*x)-1)<=1e-9&&Math.Abs(Enumerable.Range(0,3).Sum(i=>v[i+3]*v[i+6]))<=1e-9,"Orthonormal frame required.");return v;}
 static bool Same(double[] a,double[] b){return a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-9+1e-12*Math.Max(Math.Abs(x),Math.Abs(y))).All(v=>v);}
 [STAThread] static int Main(string[] args){
  if(args.Length!=7||args[0]!="--read-only-existing"||args[3]!="--expected-model-path"||args[5]!="--expected-frames-json"||File.Exists(args[2]))return 2;
  object context=null;int code=1;var report=new Dictionary<string,object>{{"state","passive_owned_workplane_failed"},{"cadMutationAttempted",false},{"selectionChangesAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"workerStarted",false}};
  try{
   string assemblyPath=Path.GetFullPath(args[1]);report["assemblySha256"]=Hash(assemblyPath);report["diagnosticSha256"]=Hash(Assembly.GetExecutingAssembly().Location);report["expectedFramesSha256"]=Hash(args[6]);
   var wanted=((IEnumerable)Json.DeserializeObject(File.ReadAllText(args[6]))).Cast<object>().Select(Values).ToArray();Need(wanted.Length>0&&wanted.Select(v=>Json.Serialize(v)).Distinct().Count()==wanted.Length,"Distinct declared frames required.");
   var assembly=Assembly.LoadFrom(assemblyPath);var config=assembly.GetType("PowerShapeBridge.Program",true).GetMethod("ReadConfig").Invoke(null,null);var type=assembly.GetType("PowerShapeBridge.DocumentBindings+ComContext",true);
   context=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{config},null);
   Func<string,object> Read=name=>type.GetMethod(name).Invoke(context,null);Func<string,object[],object> Call=(name,values)=>type.GetMethod(name).Invoke(context,values);
   var before=Json.Deserialize<Dictionary<string,object>>(Json.Serialize(Read("Observation")));report["before"]=before;report["windowsBefore"]=Read("Windows");
   Need(string.Equals(Path.GetFullPath((string)((Dictionary<string,object>)before["model"])["path"]),Path.GetFullPath(args[4]),StringComparison.OrdinalIgnoreCase),"Expected owned copy is not active.");
   Need(Json.Serialize(before["contextBefore"])=="[0,0]"&&Json.Serialize(before["contextAfter"])=="[0,0]"&&Json.Serialize(before["selectionBefore"])=="[]"&&Json.Serialize(before["selectionAfter"])=="[]","WORLD and empty selection required.");
   var entities=((IEnumerable)before["entities"]).Cast<object>().Select(v=>(Dictionary<string,object>)v).ToArray();var bodies=entities.Where(e=>Equals(e["nativeTypeCode"],40)).ToArray();var planes=entities.Where(e=>Equals(e["nativeTypeCode"],54)).ToArray();
   Need(bodies.Length==1&&Equals(bodies[0]["levelNumber"],1)&&planes.Length==wanted.Length&&entities.Length==1+wanted.Length,"One SOLID on Surfaces and the declared workplanes required.");
   int body=(int)Call("Composite",new object[]{40,(int)bodies[0]["nativeId"]});report["bodyCompositeId"]=body;report["boundsWorldRaw"]=Call("Bounds",new object[]{body});report["boundsWorldRawAgain"]=Call("Bounds",new object[]{body});
   var ids=Read("WorkplaneIds") as Array;Need(ids!=null&&ids.Rank==1&&ids.Length==wanted.Length,"Direct workplane inventory differs.");var frames=new List<object>();var matched=new HashSet<int>();
   foreach(var plane in planes){int composite=(int)Call("Composite",new object[]{54,(int)plane["nativeId"]});Need(ids.Cast<object>().Count(v=>Equals(v,composite))==1,"Direct numeric mapping differs.");var frame=Values(Call("WorkplaneInfo",new object[]{composite}));Need(Same(frame,Values(Call("WorkplaneInfo",new object[]{composite}))),"Frame changed between direct reads.");var matches=Enumerable.Range(0,wanted.Length).Where(i=>Same(frame,wanted[i])).ToArray();Need(matches.Length==1&&matched.Add(matches[0]),"Missing, duplicate or changed declared WORLD frame.");frames.Add(new Dictionary<string,object>{{"nativeId",plane["nativeId"]},{"compositeId",composite},{"worldInfo",frame},{"name",plane["name"]},{"levelNumber",plane["levelNumber"]}});}
   report["frames"]=frames;report["activeWorkplaneRaw"]=Read("ActiveWorkplane");Need(Equals(report["activeWorkplaneRaw"],0),"WORLD is not active.");var after=Json.Deserialize<Dictionary<string,object>>(Json.Serialize(Read("Observation")));report["after"]=after;report["windowsAfter"]=Read("Windows");
   foreach(string key in new[]{"model","processId","processStartedUtc","windowsSessionId","entities","entityCount","contextBefore","contextAfter","selectionBefore","selectionAfter","workplanes","levelsContainingEntities","lengthUnitsRaw"})Need(Json.Serialize(before[key])==Json.Serialize(after[key]),"Observation changed: "+key);
   Need(Json.Serialize(report["windowsBefore"])==Json.Serialize(report["windowsAfter"])&&Json.Serialize(report["boundsWorldRaw"])==Json.Serialize(report["boundsWorldRawAgain"]),"Independent readings differ.");Read("ConfirmDocument");report["state"]="passive_owned_workplane_observed";code=0;
  }catch(Exception error){report["error"]=(error.InnerException??error).ToString();}finally{if(context!=null)((IDisposable)context).Dispose();}
  report["timestampUtc"]=DateTime.UtcNow.ToString("o");using(var f=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f))w.Write(Json.Serialize(report));Console.WriteLine(Json.Serialize(new{state=report["state"],reportPath=args[2]}));return code;
 }
}
