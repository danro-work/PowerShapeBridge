using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Isolated STDIO fixture. Simulated in-memory model only; never COM/worker Main.
 internal static class OwnedLevelMoveWireFixture {
  internal static readonly string[] Faults={"control","return_false","wrong_level","lost_reply","packet_tamper"};
  static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
  static Dictionary<string,object> Read(string path){OwnedCopies.NoReparse(path);return Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
  internal static Dictionary<string,object> Admission() {
   Need(File.Exists(Path.Combine(Program.Root,".owned-level-move-wire-fixture")),"Explicit isolated admission marker required; no CAD.");
   var baseline=Read(Path.Combine(Program.Root,"bridge.config.json"));baseline["ownedLevelMovesExperimental"]=true;foreach(string key in new[]{"levelReadingExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})baseline[key]=true;
   var configs=new List<Dictionary<string,object>>();
   Action<string,Dictionary<string,object>,bool> check=(label,config,expected)=>{bool accepted=true;try{Program.ParseConfig(Program.Json.Serialize(config));}catch(ArgumentException){accepted=false;}Need(accepted==expected,"Configuration admission mismatch: "+label);configs.Add(new Dictionary<string,object>{{"case",label},{"accepted",accepted},{"expected",expected}});};
   check("enabled",new Dictionary<string,object>(baseline),true);var disabled=new Dictionary<string,object>(baseline);disabled["ownedLevelMovesExperimental"]=false;check("disabled",disabled,true);
   foreach(object wrong in new object[]{"true",1,null}){var config=new Dictionary<string,object>(baseline);config["ownedLevelMovesExperimental"]=wrong;check("wrong_flag_"+(wrong==null?"null":wrong.GetType().Name),config,false);}
   foreach(string dependency in new[]{"levelReadingExperimental","entitySetsExperimental","workingCopiesExperimental","documentBindingsExperimental"})foreach(object wrong in new object[]{false,"true"}){var config=new Dictionary<string,object>(baseline);config[dependency]=wrong;check(dependency+"_"+wrong.GetType().Name,config,false);}
   var legacy=new Dictionary<string,object>(baseline);legacy.Remove("coreProfile");check("legacy",legacy,false);
   string token=new string('a',32),sha=new string('b',64);var requests=new List<Dictionary<string,object>>();bool enabled=Program.ReadConfig().ownedLevelMovesExperimental;
   foreach(string recipe in OwnedLevelMoveOperations.Recipes)foreach(string fault in new[]{"control","extra_outer","extra_parameters","missing_parameters","bad_identity"}) {
    Dictionary<string,object> args;
    if(recipe=="prepare_owned_level_move")args=new Dictionary<string,object>{{"entitySetId",token},{"entitySetSha256",sha},{"bindingId",token},{"bindingSha256",sha},{"levelInspectionRequestId",token},{"levelInspectionSha256",sha},{"parameters",new Dictionary<string,object>{{"targetLevelNumber",5},{"targetLevelName","Named"},{"targetActiveRaw",0},{"selectionAfterPolicy","cleared_by_documented_api"},{"mode","move_in_place"}}}};
    else if(recipe=="execute_owned_level_move")args=new Dictionary<string,object>{{"ownedLevelMoveId",token},{"ownedLevelMoveSha256",sha}};
    else args=new Dictionary<string,object>{{"executionRequestId",token},{"executionReceiptSha256",sha},{"inspectionRequestId",token},{"inspectionSha256",sha},{"listingRequestId",token},{"listingSha256",sha},{"levelInspectionRequestId",token},{"levelInspectionSha256",sha},{"windowNumber",1},{"modelId",9},{"expiresUtc",DateTime.UtcNow.AddSeconds(90).ToString("o")}};
    if(fault=="extra_parameters")args["unsafe"]=true;if(fault=="bad_identity")args[args.Keys.First()]="invalid";
    string id=Guid.NewGuid().ToString("N");var envelope=new Dictionary<string,object>{{"recipeId",recipe},{"parameters",args}};if(fault=="missing_parameters")envelope.Remove("parameters");
    var request=new Dictionary<string,object>{{"requestId",id},{"operation","recipe.prepare"},{"arguments",envelope}};if(fault=="extra_outer")request["unsafe"]=true;
    bool accepted=true;try{QueueOperations.ValidateRequest(id,request);}catch(ArgumentException){accepted=false;}catch(InvalidDataException){accepted=false;}bool expected=fault=="control"&&enabled;Need(accepted==expected,"Native recipe admission mismatch.");requests.Add(new Dictionary<string,object>{{"recipe",recipe},{"fault",fault},{"accepted",accepted},{"expected",expected}});
   }
   return new Dictionary<string,object>{{"state","passed"},{"schema","psbridge.level-move-admission.v1"},{"configurations",configs},{"requests",requests},{"enabled",enabled},{"cadConnectionAttempted",false}};
  }
  internal static Dictionary<string,object> Seed(string fault) {
   Need(File.Exists(Path.Combine(Program.Root,".owned-level-move-wire-fixture")),"Explicit isolated level wire marker required; no CAD.");Need(Faults.Contains(fault),"Unknown level wire fault.");
   var f=new OwnedOperationPlanTests.Fixture(true,true);var snapshot=f.Snapshot;snapshot["busy"]=false;snapshot["levelsContainingEntities"]=new[]{new Dictionary<string,object>{{"number",0},{"name","General"},{"activeRaw",1}}};
   var hashes=Directory.GetFiles(f.Root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));
   File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(f.Root,"PSBridge.Worker.exe"));File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(f.Root,"bridge.config.json"));
   var marker=new Dictionary<string,object>{{"schema","psbridge.level-move-wire-fixture.v1"},{"fault",fault},{"protectedHashes",hashes},{"cadConnectionAttempted",false},{"sourcePath",Path.Combine(f.Root,"Source.psmodel")}};
   NativeJournal.Freeze(Path.Combine(f.Root,".level-move-wire-fixture.json"),marker);Directory.CreateDirectory(Path.Combine(f.Root,"wire-contexts"));NativeJournal.Freeze(Path.Combine(f.Root,"wire-contexts","000000.json"),new Dictionary<string,object>{{"snapshot",snapshot},{"listing",f.Listing},{"commandCalls",0},{"moved",false}});marker["root"]=f.Root;return marker;
  }
  internal static Dictionary<string,object> Run(string id) {
   Need(EntitySets.Hex(id,32),"Invalid wire request ID.");var marker=Read(Path.Combine(Program.Root,".level-move-wire-fixture.json"));Need(Equals(marker["schema"],"psbridge.level-move-wire-fixture.v1")&&Equals(marker["cadConnectionAttempted"],false)&&Faults.Contains((string)marker["fault"]),"Invalid owned wire marker.");
   string folder=Path.Combine(Program.Root,"wire-contexts");var files=Directory.GetFiles(folder,"*.json").OrderBy(path=>path,StringComparer.Ordinal).ToArray();Need(files.Length>0&&files.Length<64,"Wire context limit.");var saved=Read(files.Last());var snapshot=(Dictionary<string,object>)saved["snapshot"];
   snapshot["timestampUtc"]=DateTime.UtcNow.ToString("o");var model=(Dictionary<string,object>)snapshot["model"];
   var context=new OwnedLevelMoveExecutionTests.Context(snapshot,(Dictionary<string,object>)saved["listing"],(string)model["path"]){Calls=(int)saved["commandCalls"],Moved=(bool)saved["moved"]};
   var request=FileRequests.Parse(id,File.ReadAllText(Path.Combine(Program.Root,"requests",id+".json.processing")));QueueOperations.ValidateRequest(id,request);Need(Equals(request["operation"],"recipe.prepare"),"Only fixed recipes allowed.");
   var envelope=(Dictionary<string,object>)request["arguments"];string recipe=(string)envelope["recipeId"];var args=(Dictionary<string,object>)envelope["parameters"];Dictionary<string,object> result;
   if(recipe=="prepare_working_copy") {
    result=OwnedCopies.Create(Program.Root,id,(string)args["sourcePath"],(string)args["sourceSha256"]);var owner=(Dictionary<string,object>)result["ownership"];string working=(string)((Dictionary<string,object>)owner["working"])["path"];
    // Simulate already-open owned copy; not a qualification of OPEN/activation.
    model=(Dictionary<string,object>)context.Snapshot["model"];model["path"]=working;model["name"]=Path.GetFileNameWithoutExtension(working);context.WorkingPath=working;
    var windows=WorkingCopyOpen.Windows(context.Listing);foreach(var window in windows){window["modelPath"]=working;window["modelName"]=model["name"];}context.Listing["windows"]=windows;context.Listing["activeWindow"]=windows.Single();
   }else if(recipe=="inspect_active_model")result=context.Observation();
   else if(recipe=="list_documents") {result=context.Windows();result["state"]="documents_listed_verified";var document=new Dictionary<string,object>((Dictionary<string,object>)result["activeWindow"]);document["activationSupported"]=true;result["documents"]=new[]{document};}
   else if(recipe=="inspect_model_levels")result=NativeLevelReading.Read(context.Observation(),DateTime.UtcNow.AddSeconds(90),context);
   else if(recipe=="prepare_entity_set")result=EntitySets.InContext(id,args,true,context.Observation);
   else if(recipe=="bind_working_document")result=DocumentBindings.Prepare(Program.Root,id,args,EntitySets.Receipt(Program.Root,(string)args["inspectionRequestId"],(string)args["inspectionSha256"]),context,new Dictionary<string,object>());
   else if(recipe=="prepare_owned_level_move") {result=OwnedLevelMovePlans.Check(Program.Root,id,args,true,context);if(Equals(marker["fault"],"packet_tamper")&&result.ContainsKey("ownedLevelMoveId"))File.AppendAllText(Path.Combine(Program.Root,"ownedlevelmoves",id+".json"),"tamper");}
   else if(recipe=="execute_owned_level_move") {context.Fault=(string)marker["fault"];result=OwnedLevelMoveExecution.Execute(Program.Root,id,args,context);}
   else if(recipe=="verify_owned_level_move")result=OwnedLevelMoveExecution.Verify(Program.Root,args,context);
   else throw new ArgumentException("Unsupported level wire recipe.");
   NativeJournal.Freeze(Path.Combine(folder,files.Length.ToString("D6")+".json"),new Dictionary<string,object>{{"snapshot",context.Snapshot},{"listing",context.Listing},{"commandCalls",context.Calls},{"moved",context.Moved}});
   result["bridgeRequestId"]=id;if(!result.ContainsKey("timestampUtc"))result["timestampUtc"]=DateTime.UtcNow.ToString("o");return result;
  }
 }
}
