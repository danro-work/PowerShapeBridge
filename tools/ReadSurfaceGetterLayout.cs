using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Passive existing-reader qualification. No worker Main, Exec, selection, Save,
// activation, screenshots, verifier or fence changes. All surfaces are explicit.
class ReadSurfaceGetterLayout {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static readonly BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static readonly BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 static Dictionary<string,object> Graph(object o){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(o));}
 static string Sha(string p){using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static object Once(Func<object> read){try{return read();}catch(Exception e){return new Dictionary<string,object>{{"getterFailed",true},{"error",(e.InnerException??e).ToString()}};}}
 [STAThread] static int Main(string[] args){
  if(args.Length!=5||args[0]!="--read-only-existing"||File.Exists(args[4]))return 2;
  object context=null;int exit=1;var result=new Dictionary<string,object>{{"state","passive_surface_layout_failed"},{"cadMutationAttempted",false},{"contextChangeAttempted",false},{"cadSaveAttempted",false},{"workerStarted",false},{"selectionChangesAttempted",0}};
  try{
   var input=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));Need(Sha(args[1])==(string)input["nativeSha256"]&&Sha(args[2])==args[3],"Pinned reader/input differs.");var expected=(Dictionary<string,object>)input["observation"];DateTime expiry=DateTimeOffset.Parse((string)expected["timestampUtc"],CultureInfo.InvariantCulture).UtcDateTime.AddSeconds(300);Need(DateTime.UtcNow<expiry,"Fresh surface observation required.");
   result["inputSha256"]=args[3];result["nativeSha256"]=Sha(args[1]);result["diagnosticSha256"]=Sha(Assembly.GetExecutingAssembly().Location);
   var assembly=Assembly.LoadFrom(Path.GetFullPath(args[1]));var program=assembly.GetType("PowerShapeBridge.Program",true);var config=program.GetMethod("ReadConfig",Static).Invoke(null,null);var type=assembly.GetType("PowerShapeBridge.DocumentBindings+ComContext",true);var entities=assembly.GetType("PowerShapeBridge.EntitySets",true);var bindings=assembly.GetType("PowerShapeBridge.DocumentBindings",true);
   context=Activator.CreateInstance(type,Instance,null,new[]{config},null);var before=Graph(type.GetMethod("Observation").Invoke(context,null));result["before"]=before;var check=entities.GetMethod("CheckObserved",Static);check.Invoke(null,new object[]{expected,before,new int[0][],expiry,DateTime.UtcNow});
   var model=(Dictionary<string,object>)before["model"];Need(((IEnumerable)before["contextBefore"]).Cast<object>().Select(Convert.ToInt32).SequenceEqual(new[]{0,0}),"WORLD/empty selection required.");string working=(string)model["path"];Need(Path.GetFullPath(working)==Path.GetFullPath((string)input["workingPath"]),"Wrong owned working model.");
   var digest=bindings.GetMethod("WindowDigest",Static);string windows=(string)digest.Invoke(null,new[]{type.GetMethod("Windows").Invoke(context,null)});Need(windows==(string)digest.Invoke(null,new[]{input["listing"]}),"Pinned windows changed.");
   string workingSha=Sha(working);result["workingSha256Before"]=workingSha;Need(workingSha==(string)input["workingSha256"],"Saved bytes differ.");var inventory=((IEnumerable)before["entities"]).Cast<Dictionary<string,object>>().ToArray();Need(inventory.Length==6&&inventory.All(e=>Equals(e["nativeTypeCode"],21))&&inventory.Select(e=>e["name"]).Distinct().Count()==6,"Explicit six-SURFACE qualification source required.");
   var rows=new List<Dictionary<string,object>>();dynamic doc=type.GetField("document",Instance).GetValue(context);
   result["surfaces"]=rows;
   foreach(var entity in inventory.OrderBy(e=>(int)e["nativeId"])){
    type.GetMethod("ConfirmDocument").Invoke(context,null);int composite=(int)type.GetMethod("Composite").Invoke(context,new object[]{21,(int)entity["nativeId"]});object box=Once(()=>(object)doc.GetItemBoundingBox(composite,false));object stored=Once(()=>(object)doc.get_stored_bounding_box(composite));object grid=Once(()=>type.GetMethod("SurfacePoints").Invoke(context,new object[]{(string)entity["name"],0.0,1.0,2,0.0,1.0,2}));rows.Add(new Dictionary<string,object>{{"entityKey",new[]{21,(int)entity["nativeId"]}},{"name",entity["name"]},{"compositeId",composite},{"boundsFalseRaw",box},{"storedBoundsRaw",stored},{"surfaceGridRaw",grid}});
   }
   result["surfaces"]=rows;var after=Graph(type.GetMethod("Observation").Invoke(context,null));result["after"]=after;check.Invoke(null,new object[]{expected,after,new int[0][],expiry,DateTime.UtcNow});Need(windows==(string)digest.Invoke(null,new[]{type.GetMethod("Windows").Invoke(context,null)})&&Sha(working)==workingSha&&DateTime.UtcNow<expiry,"Surface read changed context/saved bytes or expired.");type.GetMethod("ConfirmDocument").Invoke(context,null);result["windowContextSha256"]=windows;result["workingSha256After"]=Sha(working);result["state"]="passive_six_surface_getter_layout_observed";result["scope"]="Raw GetItemBoundingBox(false), stored box and GetSurfacePoints [0,1] 2x2. No success inferred, no fallback chosen, no full geometry qualification.";exit=0;
  }catch(Exception e){result["error"]=(e.InnerException??e).ToString();}
  finally{if(context!=null)((IDisposable)context).Dispose();}
  result["timestampUtc"]=DateTime.UtcNow.ToString("o");using(var f=new FileStream(args[4],FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(f))writer.Write(Json.Serialize(result));Console.WriteLine(Json.Serialize(new{state=result["state"],error=result.ContainsKey("error")?result["error"]:null}));return exit;
 }
}
