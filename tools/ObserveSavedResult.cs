using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Standalone qualification caller for the passive core, never an MCP route.
internal static class ObserveSavedResult {
 static readonly JavaScriptSerializer J=new JavaScriptSerializer{MaxJsonLength=16000000};
 static readonly BindingFlags S=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
 static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static void Need(bool ok,string why){if(!ok)throw new InvalidDataException(why);}
 [STAThread]static int Main(string[] args){
  if(args.Length!=5||File.Exists(args[4]))return 2;
  object c=null;var report=new Dictionary<string,object>{{"state","saved_result_observation_rejected"},{"cadCommands",0},{"sdkLeaseIssued",false},{"executionAuthority",false}};int exit=1;
  try{
   Need(Hash(args[0])==args[1]&&Hash(args[2])==args[3],"Reviewed assembly/spec differs.");var spec=J.Deserialize<Dictionary<string,object>>(File.ReadAllText(args[2]));string root=(string)spec["bridgeRoot"],cfg=(string)spec["configurationPath"];
   Need(Hash(cfg)==(string)spec["configurationSha256"]&&Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.config.json"))==(string)spec["configurationSha256"],"Runtime and diagnostic config differ.");
   using(var guard=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    Need(!File.Exists(Path.Combine(root,"maintenance.json"))&&Directory.GetFiles(Path.Combine(root,"requests"),"*.json").Length==0&&Directory.GetFiles(Path.Combine(root,"requests"),"*.json.processing").Length==0,"Idle queue required.");
    var n=Assembly.LoadFrom(Path.GetFullPath(args[0]));var p=n.GetType("PowerShapeBridge.Program",true);var config=p.GetMethod("ParseConfig",S).Invoke(null,new object[]{File.ReadAllText(cfg)});
    c=Activator.CreateInstance(n.GetType("PowerShapeBridge.DocumentBindings+ComContext",true),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{config},null);
    var observed=n.GetType("PowerShapeBridge.SavedResultObservation",true).GetMethod("Observe",S).Invoke(null,new object[]{root,spec["observationId"],spec["parameters"],c});
    report["state"]="saved_result_observation_qualified_local";report["result"]=observed;report["nativeAssemblySha256"]=args[1];report["scenarioSha256"]=args[3];exit=0;
   }
  }catch(Exception error){report["error"]=(error.InnerException??error).ToString();}
  finally{if(c!=null)((IDisposable)c).Dispose();report["timestampUtc"]=DateTime.UtcNow.ToString("o");using(var f=new FileStream(args[4],FileMode.CreateNew,FileAccess.Write,FileShare.Read))using(var w=new StreamWriter(f))w.Write(J.Serialize(report));}
  Console.WriteLine(J.Serialize(new{state=report["state"],error=report.ContainsKey("error")?report["error"]:null}));return exit;
 }
}
