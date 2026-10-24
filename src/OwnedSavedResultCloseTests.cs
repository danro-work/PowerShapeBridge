using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedSavedResultCloseTests {
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".owned-saved-result-close-fixture")))throw new InvalidOperationException("Isolated saved-Result close fixture marker required; no CAD.");int checks=0;Action<bool> need=accepted=>{if(!accepted)throw new InvalidOperationException("Saved Result close guard failed "+checks);checks++;};Action<Action> rejects=action=>{bool refused=false;try{action();}catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}catch(InvalidDataException){refused=true;}catch(IOException){refused=true;}need(refused);};
   string target=new string('a',32),path=Path.Combine(Program.Root,"savedresults",target,"Result_"+target+".psmodel");var model=new Dictionary<string,object>{{"id",3},{"name","Result_"+target},{"path",path}};var first=new Dictionary<string,object>{{"windowNumber",1},{"modelId",2},{"nativeWindowId",10},{"selectedRaw",0},{"modelName","Other"},{"modelPath","Other.psmodel"}};var last=new Dictionary<string,object>{{"windowNumber",2},{"modelId",3},{"nativeWindowId",11},{"selectedRaw",1},{"modelName",model["name"]},{"modelPath",path}};var listing=new Dictionary<string,object>{{"windows",new[]{first,last}},{"windowCount",2},{"activeWindow",last}};var saved=new Dictionary<string,object>{{"path",path}};
   OwnedSavedResultClose.Target(model,listing,target,saved);checks++;
   foreach(string key in new[]{"name","path"}){var wrong=new Dictionary<string,object>(model);wrong[key]="Foreign";rejects(()=>OwnedSavedResultClose.Target(wrong,listing,target,saved));}
   var middle=new Dictionary<string,object>(listing);middle["activeWindow"]=first;rejects(()=>OwnedSavedResultClose.Target(model,middle,target,saved));var multiple=new Dictionary<string,object>(listing);multiple["windows"]=new[]{last,last};rejects(()=>OwnedSavedResultClose.Target(model,multiple,target,saved));
   var args=new Dictionary<string,object>();foreach(string k in new[]{"observationRequestId","inspectionRequestId","listingRequestId"})args[k]=new string('a',32);foreach(string k in new[]{"observationReceiptSha256","inspectionSha256","listingSha256"})args[k]=new string('b',64);args["expiresUtc"]=DateTime.UtcNow.AddSeconds(200).ToString("o");args["discardOwnedSavedResult"]=true;OwnedSavedResultClose.Validate(args,false);checks++;
   foreach(object wrong in new object[]{false,"true",1,null}){var bad=new Dictionary<string,object>(args);bad["discardOwnedSavedResult"]=wrong;rejects(()=>OwnedSavedResultClose.Validate(bad,false));}
   var extra=new Dictionary<string,object>(args){{"command","FILE CLOSE ALL"}};rejects(()=>OwnedSavedResultClose.Validate(extra,false));var v=new Dictionary<string,object>(args);v.Remove("observationRequestId");v.Remove("observationReceiptSha256");v.Remove("discardOwnedSavedResult");v["closeRequestId"]=new string('c',32);v["closeReceiptSha256"]=new string('d',64);OwnedSavedResultClose.Validate(v,true);checks++;
   foreach(var pair in new[]{new{Recipe=OwnedSavedResultClose.Recipes[0],Args=args},new{Recipe=OwnedSavedResultClose.Recipes[1],Args=v}}){string id=Guid.NewGuid().ToString("N");FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="recipe.prepare",arguments=new{recipeId=pair.Recipe,parameters=pair.Args}}));checks++;var refused=OwnedSavedResultClose.Run(id,pair.Recipe,pair.Args);need(Equals(refused["powerShapeConnectionAttempted"],false)&&Equals(refused["contextChangeAttempted"],false));}
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));need(Program.ParseConfig(Program.Json.Serialize(config)).ownedSavedResultCloseExperimental);foreach(object wrong in new object[]{"true",1,null}){var bad=new Dictionary<string,object>(config);bad["ownedSavedResultCloseExperimental"]=wrong;rejects(()=>Program.ParseConfig(Program.Json.Serialize(bad)));}
   foreach(string dependency in new[]{"ownedSavedResultRestoreExperimental","ownedSavesExperimental","workingCopiesExperimental"}){var bad=new Dictionary<string,object>(config);bad[dependency]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(bad)));}
   Directory.CreateDirectory(Path.GetDirectoryName(path));OwnedSavedResultClose.NoPending(Program.Root,target);checks++;string fence=OwnedSavedResultClose.Marker(Program.Root,target,"result-close-fence.json");File.WriteAllText(fence,"{}");rejects(()=>OwnedSavedResultClose.NoPending(Program.Root,target));File.Delete(fence);OwnedSavedResultClose.NoPending(Program.Root,target);checks++;
   return checks;
  }
 }
}
