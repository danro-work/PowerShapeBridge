using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace PowerShapeBridge {
 internal static class SavedFinalizationWindowOrderTests {
  static void Need(bool v,string why){if(!v)throw new InvalidOperationException(why);}
  static Dictionary<string,object> Clone(object o){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(o));}
  static Dictionary<string,object> Row(int n,int id,int selected){return new Dictionary<string,object>{{"windowNumber",n},{"nativeWindowId",n+10},{"type","Model"},{"selectedRaw",selected},{"modelId",id},{"modelName","Model_"+id},{"modelPath","D:\\fake\\Model_"+id+".psmodel"}};}
  static Dictionary<string,object> List(params Dictionary<string,object>[] rows){return new Dictionary<string,object>{{"processId",1},{"processStartedUtc","2026-10-27T00:00:00Z"},{"windowsSessionId",1},{"windowCount",rows.Length},{"windows",rows},{"activeWindow",rows.Single(r=>Equals(r["selectedRaw"],1))}};}
  internal static int Run(){Need(File.Exists(Path.Combine(Program.Root,".saved-finalization-ordered-fixture")),"Isolated ordered-window fixture marker required; never COM/CAD.");var cases=new List<object>();
   foreach(string fault in new[]{"same_slots","reindexed","order","path","model","name","type","selected","count","target_survives","process","duplicate_window","duplicate_native","duplicate_model","active_row"}){
    var before=List(Row(1,101,0),Row(2,202,1),Row(3,303,0));var a=Row(1,101,0);var b=Row(fault=="same_slots"?3:2,303,1);a["nativeWindowId"]=1;b["nativeWindowId"]=2;
    if(fault=="order"){a["windowNumber"]=2;b["windowNumber"]=1;}if(fault=="path")a["modelPath"]="D:\\fake\\Changed.psmodel";if(fault=="model")a["modelId"]=404;if(fault=="name")a["modelName"]="Changed";if(fault=="type")a["type"]="Drawing";if(fault=="target_survives")b["modelId"]=202;if(fault=="duplicate_window")b["windowNumber"]=a["windowNumber"];if(fault=="duplicate_native")b["nativeWindowId"]=a["nativeWindowId"];if(fault=="duplicate_model")b["modelId"]=a["modelId"];var after=List(a,b);if(fault=="selected")a["selectedRaw"]=1;if(fault=="count")after["windowCount"]=3;if(fault=="process")after["processId"]=2;if(fault=="active_row")after["activeWindow"]=Row(9,303,1);
    bool rejected=false;try{SavedFinalizationWindowOrder.Check(before,after,202);}catch(Exception e){if(!(e is InvalidDataException||e is InvalidOperationException))throw;rejected=true;}bool expected=fault!="same_slots"&&fault!="reindexed";Need(rejected==expected,"Ordered policy mismatch "+fault);cases.Add(new{fault,before,after,rejected});
   }
   string target=new string('a',32);var own=Row(2,202,1);own["modelName"]="Result_"+target;var model=new Dictionary<string,object>{{"id",202},{"name",own["modelName"]},{"path",own["modelPath"]}};var saved=new Dictionary<string,object>{{"path",own["modelPath"]}};var listing=List(Row(1,101,0),own,Row(3,303,0));OwnedSavedFinalization.Target(model,listing,target,saved,true);bool oldRejected=false;try{OwnedSavedFinalization.Target(model,listing,target,saved);}catch(InvalidDataException){oldRejected=true;}Need(oldRejected,"Legacy active-last target guard was widened.");
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","saved-finalization-ordered-tests.json"),new{state="passed",cases=cases.Count+2,rows=cases,policy=SavedFinalizationWindowOrder.Policy(),legacyNonlastRejected=true,newNonlastAccepted=true,cadConnectionAttempted=false,closeCallbacks=0});Console.WriteLine("PASS: "+(cases.Count+2)+" ordered-survivor/nonlast/legacy-policy guards; no CAD.");return cases.Count+2;
  }
 }
}
