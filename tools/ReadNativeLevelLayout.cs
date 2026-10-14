using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Attach-only level metadata diagnostic. Loads no worker Main and issues no CAD commands.
internal static class ReadNativeLevelLayout {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 static string Hash(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static object Shape(object raw){var array=raw as Array;return new{runtimeType=raw==null?null:raw.GetType().FullName,elementTypes=array==null?null:array.Cast<object>().Select(value=>value==null?null:value.GetType().FullName).Distinct().ToArray(),value=raw};}
 [STAThread] static int Main(string[] args){
  if(args.Length!=3||args[0]!="--read-only-existing")return 2;
  string assemblyPath=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);if(File.Exists(output))return 2;
  var report=new Dictionary<string,object>{{"state","passive_level_probe_failed"},{"cadCommandsAttempted",0},{"selectionChangesAttempted",0},{"cadSaveAttempted",false},{"workerStarted",false}};int code=1;
  try{
   report["assemblySha256"]=Hash(assemblyPath);report["diagnosticSha256"]=Hash(Assembly.GetExecutingAssembly().Location);
   var assembly=Assembly.LoadFrom(assemblyPath);var type=assembly.GetType("PowerShapeBridge.NativeLevelReading",true);
   var config=assembly.GetType("PowerShapeBridge.Program",true).GetMethod("ReadConfig",BindingFlags.Public|BindingFlags.Static).Invoke(null,null);
   var contextType=assembly.GetType("PowerShapeBridge.DocumentBindings+ComContext",true);object context=null,raw;
   try{
    context=Activator.CreateInstance(contextType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{config},null);
    var flags=contextType.GetMethod("LevelFilters").Invoke(context,null);report["filterLayout"]=Shape(flags);
    var fields=(object[])contextType.GetMethod("LevelFields").Invoke(context,null);report["fieldLayouts"]=fields.Select(Shape).ToArray();
    report["scalarSamples"]=new[]{0,5,997,999}.Select(number=>new{number,layout=Shape(contextType.GetMethod("LevelSample").Invoke(context,new object[]{number}))}).ToArray();
    var observation=contextType.GetMethod("Observation").Invoke(context,null);
    var expected=Json.Deserialize<Dictionary<string,object>>(Json.Serialize(observation));
    raw=type.GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{expected,DateTime.UtcNow.AddSeconds(90),context});
   }finally{if(context!=null)((IDisposable)context).Dispose();}
   var result=Json.Deserialize<Dictionary<string,object>>(Json.Serialize(raw));report["reader"]=result;
   report["state"]=Equals(result["state"],"native_levels_read_unqualified")?"passive_level_slots_observed":"passive_level_reader_rejected";
   code=Equals(report["state"],"passive_level_slots_observed")?0:1;
  }catch(Exception error){report["error"]=(error.InnerException??error).ToString();}
  report["timestampUtc"]=DateTime.UtcNow.ToString("o");
  using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(file))writer.Write(Json.Serialize(report));
  Console.WriteLine(Json.Serialize(new{state=report["state"],reportPath=output}));return code;
 }
}
