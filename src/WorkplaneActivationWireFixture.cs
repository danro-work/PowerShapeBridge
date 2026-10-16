using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class WorkplaneActivationWireFixture {
  static void Need(bool ok,string why){OwnedWorkplanePlans.Need(ok,why);}
  static Dictionary<string,object> Read(string path){OwnedCopies.NoReparse(path);return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  internal static Dictionary<string,object> Seed(bool multiple=false,bool active=false){
   Need(File.Exists(Path.Combine(Program.Root,".workplane-activation-wire-fixture")),"Isolated activation wire marker required; no CAD.");
   var f=new OwnedOperationPlanTests.Fixture(true,true);var c=multiple?OwnedWorkplaneActivationTests.SeedMultiple(f,3,active?OwnedWorkplaneActivationTests.Context.Plane:0):OwnedWorkplaneActivationTests.Seed(f,false);c.Snapshot["busy"]=false;
   var protectedHashes=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>Program.Hash(p));
   File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(f.Root,"PSBridge.Worker.exe"));File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(f.Root,"bridge.config.json"));
   var marker=new Dictionary<string,object>{{"schema","psbridge.workplane-activation-wire.v1"},{"cadConnectionAttempted",false},{"sourcePath",Path.Combine(f.Root,"Source.psmodel")},{"protectedHashes",protectedHashes}};NativeJournal.Freeze(Path.Combine(f.Root,".workplane-activation-wire.json"),marker);Directory.CreateDirectory(Path.Combine(f.Root,"wire-contexts"));
   NativeJournal.Freeze(Path.Combine(f.Root,"wire-contexts","000000.json"),new Dictionary<string,object>{{"snapshot",c.Snapshot},{"listing",c.Listing},{"setterCalls",0},{"active",c.Active}});marker["root"]=f.Root;return marker;
  }
  internal static Dictionary<string,object> Run(string id){
   Need(EntitySets.Hex(id,32),"Invalid wire request identity.");var marker=Read(Path.Combine(Program.Root,".workplane-activation-wire.json"));Need(Equals(marker["schema"],"psbridge.workplane-activation-wire.v1")&&Equals(marker["cadConnectionAttempted"],false),"Synthetic wire only.");
   string folder=Path.Combine(Program.Root,"wire-contexts");var files=Directory.GetFiles(folder,"*.json").OrderBy(p=>p,StringComparer.Ordinal).ToArray();Need(files.Length>0&&files.Length<100,"Wire context limit.");var saved=Read(files.Last());var c=new OwnedWorkplaneActivationTests.Context((Dictionary<string,object>)saved["snapshot"],(Dictionary<string,object>)saved["listing"]){Calls=(int)saved["setterCalls"],Active=(int)saved["active"]};
   var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing")));QueueOperations.ValidateRequest(id,request);Need(Equals(request["operation"],"recipe.prepare"),"Fixed recipe envelope required.");var envelope=(Dictionary<string,object>)request["arguments"];var args=(Dictionary<string,object>)envelope["parameters"];string recipe=(string)envelope["recipeId"];Dictionary<string,object> result;
   if(recipe=="prepare_working_copy"){
    result=OwnedCopies.Create(Program.Root,id,(string)args["sourcePath"],(string)args["sourceSha256"]);string path=(string)((Dictionary<string,object>)((Dictionary<string,object>)result["ownership"])["working"])["path"];var model=(Dictionary<string,object>)c.Snapshot["model"];model["path"]=path;model["name"]=Path.GetFileNameWithoutExtension(path);
    var windows=WorkingCopyOpen.Windows(c.Listing);foreach(var w in windows){w["modelPath"]=path;w["modelName"]=model["name"];}c.Listing["windows"]=windows;c.Listing["activeWindow"]=windows.Single();
   }else if(recipe=="inspect_active_model")result=c.Observation();
   else if(recipe=="list_documents"){result=c.Windows();result["state"]="documents_listed_verified";var document=new Dictionary<string,object>((Dictionary<string,object>)result["activeWindow"]);document["activationSupported"]=true;document["isActive"]=true;result["documents"]=new[]{document};}
   else if(recipe=="bind_working_document")result=DocumentBindings.Prepare(Program.Root,id,args,EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]),c,new Dictionary<string,object>());
   else if(recipe==OwnedWorkplaneActivation.Recipes[0])result=OwnedWorkplaneActivation.Check(Program.Root,id,args,true,c);
   else if(recipe==OwnedWorkplaneActivation.Recipes[1])result=OwnedWorkplaneActivation.Execute(Program.Root,id,args,c);
   else if(recipe==OwnedWorkplaneActivation.Recipes[2])result=OwnedWorkplaneActivation.Verify(Program.Root,args,c);
   else throw new ArgumentException("Unsupported activation wire recipe.");
   NativeJournal.Freeze(Path.Combine(folder,files.Length.ToString("D6")+".json"),new Dictionary<string,object>{{"snapshot",c.Observation()},{"listing",c.Listing},{"setterCalls",c.Calls},{"active",c.Active}});result["bridgeRequestId"]=id;if(!result.ContainsKey("timestampUtc"))result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
