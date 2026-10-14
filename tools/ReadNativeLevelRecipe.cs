using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

// Single fixed passive recipe only; loads the staging assembly without its Main.
internal static class ReadNativeLevelRecipe {
 [STAThread] static int Main(string[] args) {
  if(args.Length!=3||args[0]!="--read-only-existing")return 2;
  var json=new JavaScriptSerializer{MaxJsonLength=16000000};string assemblyPath=Path.GetFullPath(args[1]),id=args[2];Guid parsed;
  if(!Guid.TryParseExact(id,"N",out parsed))return 2;
  string root=AppDomain.CurrentDomain.BaseDirectory;
  string requestPath=Path.Combine(root,"requests",id+".json.processing"),output=Path.Combine(root,"responses",id+".json");
  if(File.Exists(output))return 2;
  var request=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(requestPath));
  var parameters=(Dictionary<string,object>)request["arguments"];
  if(!Equals(request["requestId"],id)||!Equals(request["operation"],"recipe.prepare")||parameters.Count!=2||!Equals(parameters["recipeId"],"inspect_model_levels")||((Dictionary<string,object>)parameters["parameters"]).Count!=0)return 2;
  var assembly=Assembly.LoadFrom(assemblyPath);var type=assembly.GetType("PowerShapeBridge.QueueOperations",true);
  type.GetMethod("ValidateRequest").Invoke(null,new object[]{id,request});
  var result=type.GetMethod("Run").Invoke(null,new object[]{id});
  Directory.CreateDirectory(Path.GetDirectoryName(output));
  using(var file=new FileStream(output,FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(file))writer.Write(json.Serialize(result));
  Console.WriteLine("Passive level recipe completed: "+id);return 0;
 }
}
