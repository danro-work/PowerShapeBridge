using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // A Bridge resource quota, not an Autodesk application-capacity assertion.
 // The legacy default stays 20. Expansion requires a current saved-source
 // observation whose complete unique-model window context is still unchanged.
 internal static class SavedReopenWindowPolicy {
  internal static Dictionary<string,object> Check(int configured,Dictionary<string,object> listing,bool current,string observedWindows) {
   if(configured<1||configured>32)throw new ArgumentException("Saved reopen model window limit must be 1..32.");
   var windows=WorkingCopyOpen.Windows(listing);int count=windows.Where(w=>w.ContainsKey("modelId")).Select(w=>w["modelId"]).Distinct().Count();
   int effective=current?configured:Math.Min(configured,20);
   if(effective>20) {
    if(windows.Length!=count||!Equals(listing["windowCount"],count)||windows.Select(w=>w["nativeWindowId"]).Distinct().Count()!=count||
       string.IsNullOrEmpty(observedWindows)||DocumentBindings.WindowDigest(listing)!=observedWindows)throw new InvalidOperationException("Expanded saved reopen requires the unchanged complete current unique-model window observation.");
   }
   if(count>=effective)throw new InvalidOperationException("Bridge saved-reopen model window quota reached.");
   return new Dictionary<string,object>{{"modelWindowLimitApplied",effective},{"modelWindowsBefore",count},{"maximumExpectedModelWindowsAfter",count+1},
    {"modelWindowAdmissionScope",effective>20?"current_saved_source_window_quota_v1":"legacy_saved_reopen_window_quota_v1"}};
  }
  internal static int SelfTest(){
   if(!File.Exists(Path.Combine(Program.Root,".saved-reopen-window-policy-fixture")))throw new InvalidOperationException("Own window-policy fixture marker required; no CAD.");
   int checks=0;Action<bool> need=ok=>{if(!ok)throw new Exception("Saved reopen quota guard failed at "+checks);checks++;};
   Func<int,Dictionary<string,object>> listing=n=>{
    var rows=Enumerable.Range(1,n).Select(i=>new Dictionary<string,object>{{"windowNumber",i},{"nativeWindowId",i+100},{"modelId",i+200},{"modelPath","Own_"+i},{"modelName","Own_"+i},{"selectedRaw",i==1?1:0},{"type","Model"}}).ToArray();
    return new Dictionary<string,object>{{"processId",1},{"processStartedUtc","2026-10-09T10:00:00Z"},{"windowsSessionId",1},{"windowCount",n},{"windows",rows},{"activeWindow",rows[0]}};
   };
   Action<Action> rejects=a=>{bool denied=false;try{a();}catch(ArgumentException){denied=true;}catch(InvalidOperationException){denied=true;}need(denied);};
   foreach(int count in new[]{1,19})need(Equals(Check(20,listing(count),false,null)["modelWindowLimitApplied"],20));
   foreach(int count in new[]{20,25,26})rejects(()=>Check(20,listing(count),false,null));
   var own=listing(25);string digest=DocumentBindings.WindowDigest(own);var accepted=Check(26,own,true,digest);need(Equals(accepted["modelWindowsBefore"],25)&&Equals(accepted["maximumExpectedModelWindowsAfter"],26)&&Equals(accepted["modelWindowLimitApplied"],26));
   rejects(()=>Check(26,own,false,digest));rejects(()=>Check(26,own,true,new string('f',64)));rejects(()=>Check(26,own,true,null));
   var full=listing(26);rejects(()=>Check(26,full,true,DocumentBindings.WindowDigest(full)));
   foreach(int limit in new[]{0,33})rejects(()=>Check(limit,own,true,digest));
   var duplicate=listing(25);((Dictionary<string,object>[])duplicate["windows"])[1]["modelId"]=201;rejects(()=>Check(26,duplicate,true,DocumentBindings.WindowDigest(duplicate)));
   var other=listing(25);((Dictionary<string,object>[])other["windows"])[1]["modelPath"]="Changed";rejects(()=>Check(26,other,true,digest));
   var large=listing(31);need(Equals(Check(32,large,true,DocumentBindings.WindowDigest(large))["maximumExpectedModelWindowsAfter"],32));
   var config=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Program.Root,"bridge.config.json")));need(Program.ParseConfig(Program.Json.Serialize(config)).savedReopenModelWindowLimit==20);
   foreach(object bad in new object[]{true,"26",null,0,33,26.5}){var changed=new Dictionary<string,object>(config);changed["savedReopenModelWindowLimit"]=bad;rejects(()=>Program.ParseConfig(Program.Json.Serialize(changed)));}
   var expanded=new Dictionary<string,object>(config);expanded["savedReopenModelWindowLimit"]=26;need(Program.ParseConfig(Program.Json.Serialize(expanded)).savedReopenModelWindowLimit==26);
   expanded["ownedSavedResultRestoreExperimental"]=false;rejects(()=>Program.ParseConfig(Program.Json.Serialize(expanded)));
   return checks;
  }
 }
}
