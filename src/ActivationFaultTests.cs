using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Explicit isolated CLI only. Opaque files and injected contexts, never COM.
 internal static class ActivationFaultTests {
  static Dictionary<string,object> Clone(Dictionary<string,object> data){var result=Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(data));result["windows"]=Program.Json.Deserialize<List<Dictionary<string,object>>>(Program.Json.Serialize(result["windows"]));return result;}
  static List<Dictionary<string,object>> Windows(Dictionary<string,object> data){return (List<Dictionary<string,object>>)data["windows"];}
  static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
  sealed class Context : DocumentSession.IActivationContext {
   internal Dictionary<string,object> Listing;internal int Calls,Reads,Disposed;internal bool Suppress,ThrowAfter,DocumentActive=true;
   internal string SelectedOverride;internal Action<int> BeforeRead;internal Action During;
   public Dictionary<string,object> Windows(){Reads++;if(BeforeRead!=null)BeforeRead(Reads);return Clone(Listing);}
   public bool IsDocumentActive(){return DocumentActive;}
   public string SelectedModelName(){return SelectedOverride??(string)((Dictionary<string,object>)Listing["activeWindow"])["modelName"];}
   public void Activate(int number){Calls++;if(!Suppress){foreach(var window in ActivationFaultTests.Windows(Listing))window["selectedRaw"]=Equals(window["windowNumber"],number)?1:0;Listing["activeWindow"]=ActivationFaultTests.Windows(Listing).Single(w=>Equals(w["windowNumber"],number));}if(During!=null)During();if(ThrowAfter)throw new IOException("Synthetic lost activation return after effect.");}
   public void Dispose(){Disposed++;}
  }
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  internal static int Run() {
   if(!File.Exists(Path.Combine(Program.Root,".activation-fault-fixture")))throw new InvalidOperationException("Explicit isolated activation fault-stage marker required; no COM attempted.");
   var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"control","already_active","stale_process","stale_target","changed_before_command","changed_process_after","changed_model_after","wrong_selected_model","document_unavailable","no_effect","lost_return_after_effect","saved_bytes_changed","native_window_changed","extra_window","expired","listing_tamper","invalid_request_id","invalid_argument","publication_before_command","publication_after_return"}) {
    string root=Path.Combine(Program.Root,"work","activation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"operations"));Directory.CreateDirectory(Path.Combine(root,"responses"));
    string a=Path.Combine(root,"A.psmodel"),b=Path.Combine(root,"B.psmodel");File.WriteAllText(a,"Opaque activation source A; never CAD.");File.WriteAllText(b,"Opaque activation source B; never CAD.");
    var first=new Dictionary<string,object>{{"windowNumber",1},{"nativeWindowId",101},{"type","Model"},{"selectedRaw",1},{"modelId",9},{"modelName","A"},{"modelPath",a}};
    var second=new Dictionary<string,object>{{"windowNumber",2},{"nativeWindowId",102},{"type","Model"},{"selectedRaw",0},{"modelId",10},{"modelName","B"},{"modelPath",b}};
    var listing=new Dictionary<string,object>{{"state","documents_listed_verified"},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"processId",123},{"processStartedUtc","2026-10-13T10:00:00Z"},{"windowsSessionId",1},{"windows",new List<Dictionary<string,object>>{first,second}},{"activeWindow",first},{"windowCount",2}};
    string listingId=Guid.NewGuid().ToString("N"),id=Guid.NewGuid().ToString("N");var args=new Dictionary<string,object>{{"listingRequestId",listingId},{"listingSha256",NativeJournal.Freeze(Path.Combine(root,"responses",listingId+".json"),listing)},{"windowNumber",2},{"modelId",10},{"expiresUtc",DateTime.UtcNow.AddSeconds(120).ToString("o")}};
    var ctx=new Context{Listing=Clone(listing)};FileStream held=null;int factoryCalls=0;
    if(fault=="already_active"){args["windowNumber"]=1;args["modelId"]=9;}
    if(fault=="stale_process")ctx.Listing["processId"]=456;
    if(fault=="stale_target")Windows(ctx.Listing)[1]["modelId"]=11;
    if(fault=="changed_before_command")ctx.BeforeRead=count=>{if(count==2)ctx.Listing["processId"]=456;};
    if(fault=="changed_process_after")ctx.During=()=>ctx.Listing["processId"]=456;
    if(fault=="changed_model_after")ctx.During=()=>Windows(ctx.Listing)[1]["modelId"]=11;
    if(fault=="wrong_selected_model")ctx.SelectedOverride="A";
    if(fault=="document_unavailable")ctx.DocumentActive=false;
    if(fault=="no_effect")ctx.Suppress=true;
    if(fault=="lost_return_after_effect")ctx.ThrowAfter=true;
    if(fault=="saved_bytes_changed")ctx.During=()=>File.AppendAllText(b," synthetic effect on own test bytes");
    if(fault=="native_window_changed")ctx.During=()=>Windows(ctx.Listing)[1]["nativeWindowId"]=999;
    if(fault=="extra_window")ctx.During=()=>{Windows(ctx.Listing).Add(new Dictionary<string,object>(first){{"fixtureExtra",true}});ctx.Listing["windowCount"]=3;};
    if(fault=="expired")args["expiresUtc"]=DateTime.UtcNow.AddSeconds(-1).ToString("o");
    if(fault=="listing_tamper")File.AppendAllText(Path.Combine(root,"responses",listingId+".json")," ");
    if(fault=="invalid_request_id")id="../not-an-operation";
    if(fault=="invalid_argument")args["windowNumber"]=true;
    if(fault=="publication_before_command")ctx.BeforeRead=count=>{if(count==2)held=new FileStream(Path.Combine(root,"operations",id+".json.tmp"),FileMode.CreateNew,FileAccess.Write,FileShare.None);};
    if(fault=="publication_after_return")ctx.During=()=>held=new FileStream(Path.Combine(root,"operations",id+".json.tmp"),FileMode.CreateNew,FileAccess.Write,FileShare.None);
    var protectedBefore=new Dictionary<string,string>{{a,Program.Hash(a)},{b,Program.Hash(b)}};Dictionary<string,object> result;string escaped=null;
    Func<DocumentSession.IActivationContext> factory=()=>{factoryCalls++;return ctx;};
    try{result=DocumentSession.Activate(root,id,args,factory);}catch(IOException error){escaped=error.Message;result=new Dictionary<string,object>{{"state","activation_publication_outcome_unknown"},{"operationId",id},{"fixtureCapturedNativeException",true}};}finally{if(held!=null)held.Dispose();}
    bool control=fault=="control"||fault=="already_active";int expectedCalls=control?fault=="already_active"?0:1:new[]{"stale_process","stale_target","changed_before_command","expired","listing_tamper","invalid_request_id","invalid_argument","publication_before_command"}.Contains(fault)?0:1;
    Require(ctx.Calls==expectedCalls,fault+": unexpected command count "+ctx.Calls+"; "+Program.Json.Serialize(result));Require(ctx.Disposed==factoryCalls,fault+": context lifetime differs.");
    Require(control?Equals(result["state"],"document_active_verified"):!Equals(result["state"],"document_active_verified"),fault+": incorrect outcome.");
    if(ctx.Calls==1&&!control)Require(((string)result["state"]).Contains("unknown"),fault+": effect must remain unknown.");
    Require(Program.Hash(a)==protectedBefore[a]&&(fault=="saved_bytes_changed"?Program.Hash(b)!=protectedBefore[b]:Program.Hash(b)==protectedBefore[b]),fault+": protected test bytes changed unexpectedly.");
    var beforeRetry=Hashes(root);int callsBefore=ctx.Calls,createdBefore=factoryCalls;var replay=DocumentSession.Activate(root,id,args,factory);
    Require(Equals(replay["state"],"rejected_before_context_change")&&ctx.Calls==callsBefore&&factoryCalls==createdBefore,fault+": replay reached context/command.");var afterRetry=Hashes(root);Require(beforeRetry.Count==afterRetry.Count&&beforeRetry.All(pair=>afterRetry.ContainsKey(pair.Key)&&afterRetry[pair.Key]==pair.Value),fault+": replay changed artifacts.");
    if(fault=="publication_after_return"){var original=Program.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"operations",id+".json")));Require(Equals(original["phase"],"before_activate"),"Original pending journal must survive interrupted completion.");}
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"operationId",id},{"parameters",args},{"commandCalls",ctx.Calls},{"contextFactoryCalls",factoryCalls},{"contextDisposals",ctx.Disposed},{"result",result},{"capturedPublicationError",escaped},{"duplicateIntent",replay},{"artifacts",afterRetry},{"savedFilesBefore",protectedBefore},{"savedFilesAfter",new Dictionary<string,string>{{a,Program.Hash(a)},{b,Program.Hash(b)}}},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","activation-fault-matrix.json"),new Dictionary<string,object>{{"schema","psbridge.activation-fault-matrix.v1"},{"state","passed"},{"nativeVersion",Program.ReleaseVersion},{"cadConnectionAttempted",false},{"rows",rows},{"scope","Injected real activation core with fake contexts and opaque files; legacy activation journal preserved, not an append-only recovery reader or full K7"}});
   Console.WriteLine("PASS: "+rows.Count+" native activation fault rows; no CAD.");return rows.Count;
  }
 }
}
