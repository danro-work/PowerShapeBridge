using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedSavedCopyCloseTests {
  internal static int Run(){
   if(!File.Exists(Path.Combine(Program.Root,".owned-saved-copy-close-fixture")))throw new InvalidOperationException("Isolated owned-close fixture marker required.");int n=0;Action<bool> need=yes=>{if(!yes)throw new Exception("Owned close guard failed at "+n);n++;};Action<Action> rejects=a=>{bool refused=false;try{a();}catch(ArgumentException){refused=true;}catch(InvalidOperationException){refused=true;}need(refused);};
   Func<int,Dictionary<string,object>> window=i=>new Dictionary<string,object>{{"windowNumber",i},{"nativeWindowId",100+i},{"modelId",200+i},{"modelName","Own_"+i},{"modelPath","Own_"+i+".psmodel"},{"selectedRaw",i==3?1:0},{"type","Model"}};
   var rows=new[]{window(1),window(2),window(3)};var before=new Dictionary<string,object>{{"processId",1},{"processStartedUtc","2026-10-24T00:00:00Z"},{"windowsSessionId",1},{"windowCount",3},{"windows",rows},{"activeWindow",rows[2]}};
   Func<Dictionary<string,object>,Dictionary<string,object>> clone=v=>Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(v));
   Func<Dictionary<string,object>> after=()=>{var value=clone(before);var left=WorkingCopyOpen.Windows(value).Take(2).ToArray();left[1]["selectedRaw"]=1;value["windowCount"]=2;value["windows"]=left;value["activeWindow"]=left[1];return value;};
   OwnedSavedCopyClose.CheckAfter(before,after(),203);n++;
   foreach(string field in new[]{"processId","processStartedUtc","windowsSessionId","windowCount"}){var v=after();v[field]=99;rejects(()=>OwnedSavedCopyClose.CheckAfter(before,v,203));}
   rejects(()=>OwnedSavedCopyClose.CheckAfter(before,before,203));rejects(()=>OwnedSavedCopyClose.CheckAfter(before,after(),201));
   foreach(string field in new[]{"modelId","modelName","modelPath","windowNumber","type"}){var v=after();((Dictionary<string,object>[])v["windows"])[0][field]="changed";rejects(()=>OwnedSavedCopyClose.CheckAfter(before,v,203));}
   var renumber=after();((Dictionary<string,object>[])renumber["windows"])[0]["nativeWindowId"]=999;OwnedSavedCopyClose.CheckAfter(before,renumber,203);n++;
   var duplicate=after();((Dictionary<string,object>[])duplicate["windows"])[0]["nativeWindowId"]=102;rejects(()=>OwnedSavedCopyClose.CheckAfter(before,duplicate,203));
   var ambiguous=after();((Dictionary<string,object>[])ambiguous["windows"])[0]["selectedRaw"]=1;rejects(()=>OwnedSavedCopyClose.CheckAfter(before,ambiguous,203));
   var args=new Dictionary<string,object>();foreach(string k in new[]{"copyId","reopenVerificationRequestId","inspectionRequestId","listingRequestId"})args[k]=new string('a',32);foreach(string k in new[]{"ownershipSha256","reopenVerificationReceiptSha256","inspectionSha256","listingSha256"})args[k]=new string('b',64);args["expiresUtc"]=DateTime.UtcNow.AddSeconds(200).ToString("o");args["discardOwnedTestCopy"]=true;OwnedSavedCopyClose.Validate(args);n++;
   foreach(object bad in new object[]{false,"true",1,null}){var v=new Dictionary<string,object>(args);v["discardOwnedTestCopy"]=bad;rejects(()=>OwnedSavedCopyClose.Validate(v));}
   var extra=new Dictionary<string,object>(args){{"command","FILE CLOSE ALL"}};rejects(()=>OwnedSavedCopyClose.Validate(extra));var foreign=new Dictionary<string,object>(args);foreign["copyId"]="../other";rejects(()=>OwnedSavedCopyClose.Validate(foreign));
   need(OwnedSavedCopyClose.Command=="FILE CLOSE SELECTED YES");
   var saved=new Dictionary<string,string>{{"Own.psmodel","abc"}};need(OwnedSavedCopyClose.SavedHashesEqual(saved,new Dictionary<string,string>(saved)));need(OwnedSavedCopyClose.SavedHashesEqual(new Dictionary<string,object>{{"Own.psmodel","abc"}},saved));need(!OwnedSavedCopyClose.SavedHashesEqual(saved,new Dictionary<string,string>{{"Own.psmodel","changed"}}));need(!OwnedSavedCopyClose.SavedHashesEqual(saved,new Dictionary<string,string>()));need(!OwnedSavedCopyClose.SavedHashesEqual(saved,new Dictionary<string,string>{{"Other.psmodel","abc"}}));
   if(Program.ReadConfig().ownedSavedCopyCloseExperimental){
    var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));var missing=new Dictionary<string,object>(config);missing.Remove("ownedSavedCopyCloseExperimental");need(!Program.ParseConfig(Program.Json.Serialize(missing)).ownedSavedCopyCloseExperimental);need(Program.ParseConfig(Program.Json.Serialize(config)).ownedSavedCopyCloseExperimental);
    foreach(object bad in new object[]{"true",1,null}){var c=new Dictionary<string,object>(config);c["ownedSavedCopyCloseExperimental"]=bad;rejects(()=>Program.ParseConfig(Program.Json.Serialize(c)));}
    foreach(string dep in new[]{"ownedSavedReopenExperimental","workingCopiesExperimental","documentBindingsExperimental"}){var c=new Dictionary<string,object>(config);c[dep]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(c)));}
    var v=new Dictionary<string,object>(args);foreach(string k in new[]{"reopenVerificationRequestId","reopenVerificationReceiptSha256","ownershipSha256","discardOwnedTestCopy"})v.Remove(k);v["closeRequestId"]=new string('c',32);v["closeReceiptSha256"]=new string('d',64);OwnedSavedCopyClose.ValidateVerification(v);n++;
    foreach(var item in new[]{new{Recipe=OwnedSavedCopyClose.Recipes[0],Args=args},new{Recipe=OwnedSavedCopyClose.Recipes[1],Args=v}}){string id=Guid.NewGuid().ToString("N");FileRequests.Parse(id,Program.Json.Serialize(new{requestId=id,operation="recipe.prepare",arguments=new{recipeId=item.Recipe,parameters=item.Args}}));n++;var fail=OwnedSavedCopyClose.Run(id,item.Recipe,item.Args);need(Equals(fail["powerShapeConnectionAttempted"],false)&&Equals(fail["contextChangeAttempted"],false));}
    var malformed=new Dictionary<string,object>(v){{"command","FILE CLOSE ALL"}};rejects(()=>OwnedSavedCopyClose.ValidateVerification(malformed));
   }
   return n;
  }
 }
}
