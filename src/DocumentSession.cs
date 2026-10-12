using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerShapeBridge {
 public static class DocumentSession {
  internal static object Attach(Config config) {
   object application=Marshal.GetActiveObject("PowerShape.Application");
   try {
    dynamic ps=application;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
    var candidates=Process.GetProcessesByName("powershape").Where(p=>p.SessionId==Process.GetCurrentProcess().SessionId).ToArray();
    if(candidates.Length!=1||candidates[0].Id!=pid)throw new InvalidOperationException("PowerShape instance absent or ambiguous.");
    if(Convert.ToBoolean(ps.Busy()))throw new InvalidOperationException("PowerShape busy.");
    string version=string.Join(".",new[]{Program.StrictInt(ps.Evaluate("VERSION.MAJOR")),Program.StrictInt(ps.Evaluate("VERSION.MINOR")),Program.StrictInt(ps.Evaluate("VERSION.REVISION"))});
    if(version!=config.expectedVersion)throw new InvalidOperationException("Unexpected PowerShape version.");
    return application;
   }catch {if(Marshal.IsComObject(application))Marshal.ReleaseComObject(application);throw;}
  }
  static Dictionary<string,object> ReadOnce(object application) {
   dynamic ps=application;int pid=Program.StrictInt(ps.Evaluate("app.pid"));
   var result=new Dictionary<string,object>{{"processId",pid}};
   using(var process=Process.GetProcessById(pid)){result["processStartedUtc"]=process.StartTime.ToUniversalTime().ToString("o");result["windowsSessionId"]=process.SessionId;}
   int count=Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"));if(count<0||count>256)throw new InvalidOperationException("Window count limit.");
   var windows=new List<Dictionary<string,object>>();
   for(int number=1;number<=4096&&windows.Count<count;number++) {
    if(Program.StrictInt(ps.Evaluate("WINDOW["+number+"].EXISTS"))!=1)continue;
    string prefix="WINDOW["+number+"]";
    int selected=Program.StrictInt(ps.Evaluate(prefix+".SELECTED"));if(selected!=0&&selected!=1)throw new InvalidOperationException("Invalid window selection state.");
    string type=Convert.ToString(ps.Evaluate(prefix+".TYPE"),CultureInfo.InvariantCulture);
    var window=new Dictionary<string,object>{{"windowNumber",number},{"nativeWindowId",Program.StrictInt(ps.Evaluate(prefix+".ID"))},{"type",type},{"selectedRaw",selected}};
    if(string.Equals(type,"MODEL",StringComparison.OrdinalIgnoreCase)) {
     string name=Convert.ToString(ps.Evaluate(prefix+".MODEL"),CultureInfo.InvariantCulture);
     if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(new[]{'\'','\r','\n'})>=0)throw new InvalidOperationException("Unsupported model name in window inventory.");
     int modelId=Program.StrictInt(ps.Evaluate("MODEL['"+name+"'].ID"));
     if(modelId<0)throw new InvalidOperationException("Invalid model ID.");
     window["modelId"]=modelId;window["modelName"]=name;window["modelPath"]=Convert.ToString(ps.Evaluate("MODEL[ID "+modelId+"].PATH"),CultureInfo.InvariantCulture);
    }
    windows.Add(window);
   }
   if(windows.Count!=count||Program.StrictInt(ps.Evaluate("WINDOW.NUMBER"))!=count)throw new InvalidOperationException("Incomplete/changing window inventory.");
   if(count>0&&windows.Count(w=>Equals(w["selectedRaw"],1))!=1)throw new InvalidOperationException("Ambiguous selected window.");
   var documents=new List<Dictionary<string,object>>();
   foreach(var group in windows.Where(w=>w.ContainsKey("modelId")).GroupBy(w=>(int)w["modelId"])) {
    var first=group.First();
    if(group.Any(w=>!Equals(w["modelName"],first["modelName"])||!Equals(w["modelPath"],first["modelPath"])))throw new InvalidOperationException("Ambiguous document identity.");
    documents.Add(new Dictionary<string,object>{{"modelId",first["modelId"]},{"name",first["modelName"]},{"path",first["modelPath"]},
     {"isActive",group.Any(w=>Equals(w["selectedRaw"],1))},{"activationSupported",group.Count()==1},
     {"windowNumber",group.Count()==1?first["windowNumber"]:null},{"windowNumbers",group.Select(w=>(int)w["windowNumber"]).ToArray()}});
   }
   result["windowCount"]=count;result["windows"]=windows;result["documents"]=documents;
   result["activeWindow"]=windows.SingleOrDefault(w=>Equals(w["selectedRaw"],1));return result;
  }
  internal static Dictionary<string,object> ReadStable(object application) {
   var first=ReadOnce(application);var second=ReadOnce(application);
   if(Program.Json.Serialize(first)!=Program.Json.Serialize(second))throw new InvalidOperationException("Window/document context changed during read.");
   return second;
  }
  public static Dictionary<string,object> List() {
   object application=null;
   try {application=Attach(Program.ReadConfig());var result=ReadStable(application);
    result["state"]="documents_listed_verified";result["bridgeVersion"]=Program.ReleaseVersion;result["timestampUtc"]=DateTime.UtcNow.ToString("o");
    result["geometryMutationAttempted"]=false;result["contextChangeAttempted"]=false;
    result["qualification"]="Window/model metadata read twice; no activation, save, selection or capture commands. Window numbers/IDs are not persistent.";
    return result;
   }catch(Exception error){return new Dictionary<string,object>{{"state","document_list_failed"},{"error",error.Message},{"geometryMutationAttempted",false},{"contextChangeAttempted",false}};}
   finally {if(application!=null&&Marshal.IsComObject(application))Marshal.ReleaseComObject(application);}
  }
  public static void ValidateActivation(Dictionary<string,object> args) {
   Guid guid;
   if(args.Count!=5||!args.ContainsKey("listingRequestId")||!(args["listingRequestId"] is string)||!Guid.TryParseExact((string)args["listingRequestId"],"N",out guid)||
      !args.ContainsKey("listingSha256")||!(args["listingSha256"] is string)||((string)args["listingSha256"]).Length!=64||((string)args["listingSha256"]).Any(x=>!((x>='0'&&x<='9')||(x>='a'&&x<='f')))||
      !args.ContainsKey("windowNumber")||!(args["windowNumber"] is int)||!args.ContainsKey("modelId")||!(args["modelId"] is int)||
      !args.ContainsKey("expiresUtc")||!(args["expiresUtc"] is string))throw new ArgumentException("Invalid document activation lease.");
   if((int)args["windowNumber"]<1||(int)args["windowNumber"]>4096||(int)args["modelId"]<0)throw new ArgumentException("Invalid native document/window identity.");
   DateTime.Parse((string)args["expiresUtc"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
  }
  static string Key(Dictionary<string,object> snapshot) {
   return Program.Json.Serialize(new object[]{snapshot["processId"],snapshot["processStartedUtc"],snapshot["windowsSessionId"],snapshot["windows"]});
  }
  static Dictionary<string,string> HashSavedFiles(Dictionary<string,object>[] windows) {
   var hashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var window in windows.Where(w=>w.ContainsKey("modelPath"))) {
    string path=(string)window["modelPath"];
    if(!string.IsNullOrWhiteSpace(path)&&File.Exists(path))hashes[path]=Program.Hash(path);
   }
   return hashes;
  }
  public static Dictionary<string,object> Activate(string requestId,Dictionary<string,object> args) {
   var result=new Dictionary<string,object>{{"operationId",requestId},{"state","rejected_before_context_change"},{"geometryMutationAttempted",false},{"contextChangeAttempted",false}};
   object application=null;string journal=Path.Combine(Program.Root,"operations",requestId+".json");bool attempted=false;
   if(File.Exists(journal)||File.Exists(Path.Combine(Program.Root,"operations",requestId+".once"))) {
    result["error"]="Activation request already recorded; replay forbidden.";result["originalJournalPreserved"]=true;return result;
   }
   try {
    ValidateActivation(args);DateTime expiry=DateTime.Parse((string)args["expiresUtc"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
    string listingPath=Path.Combine(Program.Root,"responses",(string)args["listingRequestId"]+".json");
    if(Program.Hash(listingPath)!=(string)args["listingSha256"])throw new IOException("Document listing receipt changed.");
    var expected=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(listingPath,Encoding.UTF8));
    if(!Equals(expected["state"],"documents_listed_verified"))throw new InvalidOperationException("Verified listing required.");
    DateTime listedUtc=DateTime.Parse((string)expected["timestampUtc"],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
    if(DateTime.UtcNow>=expiry||expiry>listedUtc.AddSeconds(300)||expiry<listedUtc)throw new InvalidOperationException("Document lease expired or invalid.");
    using(var guard=new FileStream(Path.Combine(Program.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
     application=Attach(Program.ReadConfig());dynamic ps=application;var before=ReadStable(application);
     if(Key(before)!=Key(expected))throw new InvalidOperationException("Document/window context changed since listing.");
     var windows=(List<Dictionary<string,object>>)before["windows"];
     var target=windows.SingleOrDefault(w=>Equals(w["windowNumber"],args["windowNumber"])&&w.ContainsKey("modelId")&&Equals(w["modelId"],args["modelId"]));
     if(target==null||windows.Count(w=>w.ContainsKey("modelId")&&Equals(w["modelId"],args["modelId"]))!=1)throw new InvalidOperationException("Target model window absent or ambiguous.");
     var savedHashes=HashSavedFiles(windows.ToArray());result["savedFileHashesBefore"]=savedHashes;result["listingRequestId"]=args["listingRequestId"];result["listingSha256"]=args["listingSha256"];
     result["before"]=before;result["target"]=target;
     using(var once=new FileStream(Path.Combine(Program.Root,"operations",requestId+".once"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
     if(Key(ReadStable(application))!=Key(expected)||DateTime.UtcNow>=expiry)throw new InvalidOperationException("Context/lease changed immediately before activation.");
     bool alreadyActive=Equals(target["selectedRaw"],1);result["alreadyActive"]=alreadyActive;
     if(!alreadyActive) {
      attempted=true;result["contextChangeAttempted"]=true;result["state"]="context_change_outcome_pending";TranslationTest.Journal(journal,result,"before_activate");
      ps.Exec("Sel_Window @"+(int)args["windowNumber"]+" Front");
     }
     var after=ReadStable(application);result["after"]=after;
     var active=(Dictionary<string,object>)after["activeWindow"];
     if(active==null||!active.ContainsKey("modelId")||!Equals(active["modelId"],args["modelId"])||!Equals(active["windowNumber"],args["windowNumber"]))throw new InvalidOperationException("Requested document did not become active.");
     if(!Convert.ToBoolean(ps.IsDocumentActive())||Convert.ToString(ps.Evaluate("MODEL.SELECTED"),CultureInfo.InvariantCulture)!=(string)target["modelName"])throw new InvalidOperationException("Active document and selected window disagree.");
     var afterWindows=(List<Dictionary<string,object>>)after["windows"];
     foreach(var window in afterWindows)window["selectedRaw"]=Equals(window["windowNumber"],before["activeWindow"]==null?null:((Dictionary<string,object>)before["activeWindow"])["windowNumber"])?1:0;
     if(Key(after)!=Key(before))throw new InvalidOperationException("Window/document metadata changed beyond activation.");
     // Restore true selected flags in the reported snapshot after the comparison above.
     foreach(var window in afterWindows)window["selectedRaw"]=Equals(window["windowNumber"],args["windowNumber"])?1:0;
     foreach(var hash in savedHashes)if(Program.Hash(hash.Key)!=hash.Value)throw new IOException("An open saved project changed on disk during activation.");
     result["savedFilesUnchanged"]=true;result["state"]="document_active_verified";
     result["verificationScope"]="Native model/window identity and saved-file hashes checked; only Sel_Window Front may have been issued. No geometry-edit/save/close commands; full UI state and geometry topology not certified.";
     TranslationTest.Journal(journal,result,"completed");return result;
    }
   }catch(Exception error){result["state"]=attempted?"context_change_outcome_unknown":"rejected_before_context_change";result["error"]=error.Message;result["recoveryInstructions"]="Read document list and operation journal; never automatically repeat an unknown activation.";TranslationTest.Journal(journal,result,"stopped");return result;}
   finally {if(application!=null&&Marshal.IsComObject(application))Marshal.ReleaseComObject(application);}
  }
 }
}
