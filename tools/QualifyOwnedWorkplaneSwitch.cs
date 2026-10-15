using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Bounded API qualification, not a published MCP tool. No CAD access unless
// --read-only-preflight or separately approved --execute is explicitly selected.
internal static class QualifyOwnedWorkplaneSwitch {
 static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16000000};
 const string WorkerSha="4f2eae9f051fa8c3e829e3670629b4ae71a37e037808d5e2aa3fc26c5decadc7";
 const string ReaderSha="a8ba27073d80c7f4858d29eceed7647bb03b6aca881b621a22ca949846ea0853";
 const BindingFlags All=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static Dictionary<string,object> Map(object value){return Json.Deserialize<Dictionary<string,object>>(Json.Serialize(value));}
 static object[] Items(object value){Need(value is IEnumerable&&!(value is string),"Array required.");return ((IEnumerable)value).Cast<object>().ToArray();}
 static Dictionary<string,object>[] Rows(object value){return Items(value).Select(Map).ToArray();}
 static Dictionary<string,object> Load(string path){return Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));}
 static string Hash(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 static void Need(bool value,string why){if(!value)throw new InvalidDataException(why);}
 static bool Same(object a,object b){return Json.Serialize(a)==Json.Serialize(b);}
 static bool SamePath(string a,string b){return string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);}
 static string Freeze(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(path));byte[] bytes=System.Text.Encoding.UTF8.GetBytes(Json.Serialize(value));using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}return Hash(path);}
 static object Invoke(Type type,string name,object target,params object[] args){try{return type.GetMethod(name,All).Invoke(target,args);}catch(TargetInvocationException error){throw error.InnerException??error;}}

 internal interface IContext:IDisposable {
  Dictionary<string,object> Observation();Dictionary<string,object> Windows();void Confirm();
  int Active();int Composite(int type,int native);object WorkplaneIds();double[] Frame(int composite);double[] Bounds(int composite);
  void Activate(int composite);
 }
 sealed class Com:IContext {
  readonly Type type;object context;
  internal Com(Assembly worker){type=worker.GetType("PowerShapeBridge.DocumentBindings+ComContext",true);object config=Invoke(worker.GetType("PowerShapeBridge.Program",true),"ReadConfig",null);context=Activator.CreateInstance(type,All,null,new[]{config},null);}
  object Call(string name,params object[] args){return Invoke(type,name,context,args);}
  public Dictionary<string,object> Observation(){return Map(Call("Observation"));}
  public Dictionary<string,object> Windows(){return Map(Call("Windows"));}
  public void Confirm(){Call("ConfirmDocument");}
  public int Active(){return (int)Call("ActiveWorkplane");}
  public int Composite(int kind,int native){return (int)Call("Composite",kind,native);}
  public object WorkplaneIds(){return Call("WorkplaneIds");}
  public double[] Frame(int composite){return Numbers(Call("WorkplaneInfo",composite),9);}
  public double[] Bounds(int composite){return Numbers(Call("Bounds",composite),6);}
  public void Activate(int composite){Need(composite>=0,"Nonnegative explicit composite required.");Confirm();dynamic document=type.GetField("document",All).GetValue(context);document.ActiveWorkplane=composite;}
  public void Dispose(){if(context!=null)((IDisposable)context).Dispose();context=null;}
 }
 static double[] Numbers(object value,int count){var array=Items(value);Need(array.Length==count&&array.All(v=>v is int||v is long||v is double||v is float||v is decimal),"Finite numeric vector required.");var result=array.Select(Convert.ToDouble).ToArray();Need(result.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)),"Nonfinite coordinate.");return result;}
 static bool Near(double[] a,double[] b){return a.Length==b.Length&&a.Zip(b,(x,y)=>Math.Abs(x-y)<=1e-9+1e-12*Math.Max(Math.Abs(x),Math.Abs(y))).All(v=>v);}
 static double[] Local(double[] world,double[] frame,bool point){var z=frame.Skip(3).Take(3).ToArray();var y=frame.Skip(6).Take(3).ToArray();var x=new[]{y[1]*z[2]-y[2]*z[1],y[2]*z[0]-y[0]*z[2],y[0]*z[1]-y[1]*z[0]};var d=world.Select((v,i)=>v-(point?frame[i]:0)).ToArray();return new[]{x.Zip(d,(a,b)=>a*b).Sum(),y.Zip(d,(a,b)=>a*b).Sum(),z.Zip(d,(a,b)=>a*b).Sum()};}
 static string Coordinates(object value,object original,double[] frame,bool point){var a=Numbers(value,3);var b=Numbers(original,3);bool world=Near(a,b),local=Near(a,Local(b,frame,point));Need(world||local,"Coordinates match neither unchanged WORLD values nor the declared active frame.");return world&&local?"world_or_active_same_values":world?"world_observed":"active_frame_observed";}
 static void Empty(Dictionary<string,object> observation,int active){Need(Same(observation["contextBefore"],new[]{0,active})&&Same(observation["contextAfter"],new[]{0,active})&&Same(observation["selectionBefore"],new object[0])&&Same(observation["selectionAfter"],new object[0]),"Expected active frame and empty selection.");}

 internal static Dictionary<string,object> Capture(IContext context){
  context.Confirm();var snapshot=context.Observation();var windows=context.Windows();var entities=Rows(snapshot["entities"]);
  Need(entities.Length==2&&entities.Count(v=>Equals(v["nativeTypeCode"],40))==1&&entities.Count(v=>Equals(v["nativeTypeCode"],54))==1,"Qualification requires one SOLID and one existing WORKPLANE.");
  var body=entities.Single(v=>Equals(v["nativeTypeCode"],40));var plane=entities.Single(v=>Equals(v["nativeTypeCode"],54));
  Need(Equals(body["levelNumber"],1)&&Equals(plane["levelNumber"],0),"Expected owned fixture levels.");
  int target=context.Composite(54,(int)plane["nativeId"]),solid=context.Composite(40,(int)body["nativeId"]);
  var ids=Items(context.WorkplaneIds());Need(ids.Length==1&&ids[0] is int&&(int)ids[0]==target&&target>0,"Direct workplane composite mapping differs.");
  var frame=context.Frame(target);Need(frame.Length==9,"Frame getter must return nine values.");
  var bounds=context.Bounds(solid);Need(bounds.Length==6&&bounds.All(v=>!double.IsNaN(v)&&!double.IsInfinity(v)),"WORLD bounding getter unavailable.");
  context.Confirm();return new Dictionary<string,object>{{"observation",snapshot},{"windows",windows},{"activeRaw",context.Active()},{"targetCompositeId",target},{"bodyCompositeId",solid},{"frameRaw",frame},{"boundsFalseRaw",bounds}};
 }
 static void ContextStable(Dictionary<string,object> before,Dictionary<string,object> after){
  var a=Map(before["observation"]);var b=Map(after["observation"]);
  foreach(string key in new[]{"model","processId","processStartedUtc","windowsSessionId","productVersion","entityCount","countsByType","levelsContainingEntities","lengthUnitsRaw","lengthUnitNameRaw","lengthUnitFactorRaw","areaUnitNameRaw","areaUnitFactorRaw","volumeUnitNameRaw","volumeUnitFactorRaw","generalToleranceRaw"})Need(Same(a[key],b[key]),"Context or unit change: "+key);
  var wa=Map(before["windows"]);var wb=Map(after["windows"]);wa.Remove("timestampUtc");wb.Remove("timestampUtc");Need(Same(wa,wb),"Window context changed.");
  Need(Equals(before["targetCompositeId"],after["targetCompositeId"])&&Equals(before["bodyCompositeId"],after["bodyCompositeId"]),"Composite mapping changed.");
 }
 internal static Dictionary<string,object> CheckActive(Dictionary<string,object> before,Dictionary<string,object> after){
  ContextStable(before,after);int target=(int)before["targetCompositeId"];Need(Equals(after["activeRaw"],target),"Declared workplane is not active.");
  var a=Map(before["observation"]);var b=Map(after["observation"]);Empty(a,0);Empty(b,target);
  Need(Near(Numbers(before["boundsFalseRaw"],6),Numbers(after["boundsFalseRaw"],6)),"GetItemBoundingBox(false) changed under activation.");
  var frame=Numbers(before["frameRaw"],9);var modes=new Dictionary<string,object>();
  var expectedFrame=Local(frame.Take(3).ToArray(),frame,true).Concat(Local(frame.Skip(3).Take(3).ToArray(),frame,false)).Concat(Local(frame.Skip(6).Take(3).ToArray(),frame,false)).ToArray();
  bool worldFrame=Near(Numbers(after["frameRaw"],9),frame),localFrame=Near(Numbers(after["frameRaw"],9),expectedFrame);Need(worldFrame||localFrame,"Direct frame getter is inconsistent with original world geometry.");modes["GetWorkplaneInfo"]=worldFrame?"world_observed":"active_frame_observed";
  var oldEntities=Rows(a["entities"]);var newEntities=Rows(b["entities"]);
  foreach(var old in oldEntities){var current=newEntities.Single(v=>Equals(v["nativeTypeCode"],old["nativeTypeCode"])&&Equals(v["nativeId"],old["nativeId"]));
   if(Equals(old["nativeTypeCode"],40)){
    var original=Map(old["geometry"]);var geometry=Map(current["geometry"]);Need(Equals(original["coordinateFrame"],"world")&&Equals(geometry["coordinateFrame"],"active_workplane_unqualified"),"Probe frame label differs.");
    modes["solidCOG"]=Coordinates(geometry["cogRaw"],original["cogRaw"],frame,true);geometry["cogRaw"]=original["cogRaw"];geometry["coordinateFrame"]=original["coordinateFrame"];current["geometry"]=geometry;
   }
   Need(Same(old,current),"Entity metadata or nonspatial properties changed.");
  }
  var oldPlanes=Rows(a["workplanes"]);var newPlanes=Rows(b["workplanes"]);Need(oldPlanes.Length==1&&newPlanes.Length==1,"Workplane observations differ.");
  foreach(string key in new[]{"originRaw","xaxisRaw","yaxisRaw","zaxisRaw"}){modes[key]=Coordinates(newPlanes[0][key],oldPlanes[0][key],frame,key=="originRaw");newPlanes[0][key]=oldPlanes[0][key];}
  Need(Same(oldPlanes,newPlanes),"Workplane metadata changed.");return modes;
 }
 internal static void CheckWorld(Dictionary<string,object> before,Dictionary<string,object> after){
  ContextStable(before,after);Need(Equals(after["activeRaw"],0),"WORLD not active.");
  var a=Map(before["observation"]);var b=Map(after["observation"]);Empty(a,0);Empty(b,0);
  foreach(string key in a.Keys.Where(k=>k!="timestampUtc"&&k!="durationMs"&&k!="elapsedMs"&&k!="warnings"&&k!="probeStages")){
   // The native digest below checks its full normative observed field set.
   if(new[]{"entities","workplanes","contextBefore","contextAfter","selectionBefore","selectionAfter"}.Contains(key))Need(Same(a[key],b[key]),"WORLD observation changed: "+key);
  }
  Need(Near(Numbers(before["frameRaw"],9),Numbers(after["frameRaw"],9))&&Near(Numbers(before["boundsFalseRaw"],6),Numbers(after["boundsFalseRaw"],6)),"WORLD frame/bounds differ after return.");
 }
 internal static Dictionary<string,object> Pair(IContext context,Dictionary<string,object> baseline,Func<string,object,string> freeze,Action guard){
  int target=(int)baseline["targetCompositeId"];Empty(Map(baseline["observation"]),0);Need(context.Active()==0,"WORLD required before activation.");
  CheckWorld(baseline,Capture(context));guard();context.Confirm();Need(context.Active()==0,"WORLD changed at the command boundary.");context.Activate(target);Need(context.Active()==target,"Setter did not return with target active. No WORLD command is allowed.");
  string first=freeze("activation-return.json",new{targetCompositeId=target,activeRaw=context.Active(),setterReturned=true,timestampUtc=DateTime.UtcNow.ToString("o")});
  var active=Capture(context);var modes=CheckActive(baseline,active);CheckActive(baseline,Capture(context));guard();context.Confirm();Need(context.Active()==target,"Active workplane changed before declared WORLD return.");
  context.Activate(0);Need(context.Active()==0,"WORLD setter return is unproven.");
  string second=freeze("world-return.json",new{targetCompositeId=0,activeRaw=0,setterReturned=true,timestampUtc=DateTime.UtcNow.ToString("o")});
  var world=Capture(context);CheckWorld(baseline,world);CheckWorld(baseline,Capture(context));guard();
  return new Dictionary<string,object>{{"activeObservation",active},{"worldObservation",world},{"coordinateModesObserved",modes},{"activationReturnSha256",first},{"worldReturnSha256",second},{"setterCalls",2}};
 }

 sealed class Native {
  internal readonly Assembly Assembly;internal readonly string Root;readonly Type journal;
  internal Native(string path){Need(Hash(path)==WorkerSha,"Only the qualified installed worker is accepted.");Root=Path.GetDirectoryName(Path.GetFullPath(path));string config=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.config.json");Need(Hash(config)==Hash(Path.Combine(Root,"bridge.config.json")),"Diagnostic configuration must exactly match the installed runtime.");var flags=Load(config);Need(Equals(flags["coreProfile"],"general-v1")&&Equals(flags["captureViewport"],false),"General configuration without capture required.");Assembly=System.Reflection.Assembly.LoadFrom(Path.GetFullPath(path));journal=Assembly.GetType("PowerShapeBridge.NativeJournal",true);}
  internal object Call(string type,string name,params object[] args){return Invoke(Assembly.GetType("PowerShapeBridge."+type,true),name,null,args);}
  internal string Digest(Dictionary<string,object> value){return (string)Call("EntitySets","ObservedDigest",value);}
  internal IDisposable Hold(string copy,string ownership){return (IDisposable)Call("OwnedCopies","Hold",Root,copy,ownership);}
  internal void Recheck(IDisposable lease){Invoke(lease.GetType(),"Recheck",lease);}
  internal string Saved(Dictionary<string,object> windows){return Json.Serialize(Call("WorkingCopyOpen","SavedHashes",windows));}
  internal object Journal(string root,string id,string sha){return Activator.CreateInstance(journal,All,null,new object[]{root,id,sha},null);}
  internal void Append(object value,string state,Dictionary<string,object> details){Invoke(journal,"Append",value,state,details);}
  internal object Resume(string root,string id,string sha){return Invoke(journal,"ForVerification",null,root,id,sha);}
 }

 [STAThread] static int Main(string[] args){
  try{
   if(args.Length==2&&args[0]=="--self-test")return SelfTest(args[1]);
   if(args.Length==4&&args[0]=="--self-test-files")return SelfTestFiles(args[1],args[2],args[3]);
   if(args.Length==5&&args[0]=="--verify-files-only")return Verify(args[1],args[2],args[3],args[4]);
   Need((args.Length==4&&args[0]=="--read-only-preflight")||(args.Length==5&&args[0]=="--execute"),"Use --read-only-preflight worker source-audit new-output; --execute additionally requires approval-note. No default CAD action.");
   return Run(args[0]=="--execute",args[1],args[2],args[3],args.Length==5?args[4]:null);
  }catch(Exception error){Console.Error.WriteLine((error.InnerException??error).ToString());return 1;}
 }

 static int Run(bool execute,string worker,string source,string output,string approval){
  var native=new Native(worker);string root=native.Root;output=Path.GetFullPath(output);Need(output.StartsWith(Path.GetFullPath(Path.Combine(root,"..","..","evidence"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&!Directory.Exists(output)&&!File.Exists(output),"New project evidence directory required.");
  native.Call("OwnedCopies","NoReparse",output);native.Call("OwnedCopies","NoReparse",Path.GetFullPath(source));
  var audit=Load(source);Need(Equals(audit["state"],"passed"),"Passed owned-copy persistence audit required.");string copy=(string)audit["newCopyId"],working=(string)audit["newWorkingPath"];
  Need(copy.Length==32&&copy.All(c=>"0123456789abcdef".Contains(c)),"Invalid owned-copy token.");Need(SamePath(working,Path.Combine(root,"workingcopies",copy,"Working_"+copy+".psmodel")),"Owned working path mismatch.");
  var original=Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"result.json"));Need(Hash(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"result.json"))==(string)audit["reportSha256"],"Original driver report changed.");
  string independentSource=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"independent-native-result.json");Need(Hash(independentSource)==(string)audit["freshReadSha256"],"Original independent read changed.");Need(Hash(working)==(string)audit["savedResultSha256"],"Saved working bytes differ from the accepted result.");
  var protectedHashes=Map(original["protectedHashes"]);foreach(var p in Directory.GetFiles(Path.Combine(root,"operations",(string)audit["saveOperationId"]),"*.json").Concat(Directory.GetFiles(Path.Combine(root,"operations",(string)audit["reopenOperationId"]),"*.json")))protectedHashes[p]=Hash(p);
  foreach(string p in new[]{source,independentSource,Path.Combine(root,"workingcopies",copy,"ownership.json"),working,Path.Combine(root,"workingcopies",copy,"checkpoint","Before.psmodel")})protectedHashes[Path.GetFullPath(p)]=Hash(p);
  string owner=Hash(Path.Combine(root,"workingcopies",copy,"ownership.json"));string fence=Path.Combine(root,"workingcopies",copy,"mutation-fence.json");string once=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(root)),"work","workplane-switch-live-submission.once");
  Need(!File.Exists(fence)&&!Directory.Exists(fence),"Existing owned-copy fence retained.");
  if(execute){Need(File.Exists(approval)&&new FileInfo(approval).Length>0,"Separate explicit approval note required.");Need(!File.Exists(once)&&!Directory.Exists(once),"Qualification already claimed; inspect original outcome, never replay.");}
  Need(!Directory.GetFiles(Path.Combine(root,"requests")).Any(p=>p.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".processing",StringComparison.OrdinalIgnoreCase)),"Bridge queue must be idle.");
  Directory.CreateDirectory(output);var report=new Dictionary<string,object>{{"state","qualification_started"},{"executionRequested",execute},{"cadMutationAttempted",false},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"automaticReplayForbidden",true},{"workerStarted",false},{"workerSha256",Hash(worker)},{"sourceAuditPath",Path.GetFullPath(source)},{"sourceAuditSha256",Hash(source)},{"copyId",copy},{"ownershipSha256",owner},{"protectedHashes",protectedHashes},{"qualificationRoot",output},{"runtimeRoot",root},{"startedUtc",DateTime.UtcNow.ToString("o")}};
  object journal=null;string operation=Guid.NewGuid().ToString("N"),planSha=null,fenceSha=null;bool attempted=false;
  try{
   using(var serial=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
   using(var lease=native.Hold(copy,owner))
   using(var context=new Com(native.Assembly)){
    var baseline=Capture(context);var observed=Map(baseline["observation"]);Empty(observed,0);Need(Equals(baseline["activeRaw"],0)&&SamePath((string)Map(observed["model"])["path"],working),"Expected newly reopened owned model must be active in WORLD.");
    Need(Equals(Map(baseline["windows"])["windowCount"],9),"Expected nine existing windows.");Need(Near(Numbers(baseline["frameRaw"],9),Numbers(audit["workplaneWorldInfo"],9)),"Expected saved frame differs.");
    var sourceRead=Load(independentSource);Need(native.Digest(observed)==native.Digest(Map(sourceRead["after"])),"Current observed owned model differs from independently saved result.");
    string saved=native.Saved(Map(baseline["windows"]));DateTime deadline=DateTime.UtcNow.AddSeconds(120);Action guard=()=>{Need(DateTime.UtcNow<deadline,"Qualification guard expired; no further setter allowed.");native.Recheck(lease);Need(Hash(worker)==WorkerSha&&Hash(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge.config.json"))==Hash(Path.Combine(root,"bridge.config.json")),"Runtime or diagnostic configuration changed.");foreach(var p in protectedHashes)Need(Hash(p.Key)==(string)p.Value,"Protected file changed.");Need(saved==native.Saved(context.Windows()),"Existing saved document bytes changed.");context.Confirm();};
    guard();Need(native.Digest(observed)==native.Digest(context.Observation()),"Current context changed before action.");report["before"]=baseline;
    if(!execute){report["state"]="owned_workplane_switch_read_only_ready";return 0;}
    var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",operation},{"operation","qualification_existing_workplane_then_world"},{"copyId",copy},{"ownershipSha256",owner},{"before",baseline},{"sourceAuditSha256",Hash(source)},{"approvalSha256",Hash(approval)},{"maximumSetterCalls",2},{"noAutomaticRecovery",true},{"createdUtc",DateTime.UtcNow.ToString("o")}};
    planSha=Freeze(Path.Combine(output,"plan.json"),plan);journal=native.Journal(output,operation,planSha);native.Append(journal,"prepared",new Dictionary<string,object>{{"qualificationOnly",true}});
    fenceSha=Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",operation},{"planSha256",planSha},{"copyId",copy},{"ownershipSha256",owner},{"automaticReplayForbidden",true}});
    string claim=Freeze(once,new{operationId=operation,planSha256=planSha,qualificationRoot=output,approvalSha256=Hash(approval),automaticReplayForbidden=true});
    report["operationId"]=operation;report["planSha256"]=planSha;report["executionFenceSha256"]=fenceSha;report["claimPath"]=once;report["claimSha256"]=claim;
    native.Append(journal,"execution_claimed",new Dictionary<string,object>{{"qualificationOnly",true}});guard();Need(native.Digest(observed)==native.Digest(context.Observation()),"Pre-command context changed.");
    native.Append(journal,"command_started",new Dictionary<string,object>{{"maximumSetterCalls",2},{"firstTarget",baseline["targetCompositeId"]},{"secondTarget",0}});attempted=true;report["cadMutationAttempted"]=report["contextChangeAttempted"]=true;
    var pair=Pair(context,baseline,(name,value)=>Freeze(Path.Combine(output,name),value),guard);report["result"]=pair;
    native.Append(journal,"command_returned",new Dictionary<string,object>{{"nativeRequestId",operation},{"activationReturnSha256",pair["activationReturnSha256"]},{"worldReturnSha256",pair["worldReturnSha256"]},{"worldActivationReturned",true},{"setterCalls",2}});
    Need(native.Digest(observed)==native.Digest(Map(Map(pair["worldObservation"])["observation"])),"Full native observed digest differs after WORLD return.");
    native.Append(journal,"executed_unverified",new Dictionary<string,object>{{"independentVerificationRequired",true},{"qualificationOnly",true}});report["state"]="owned_workplane_switch_executed_unverified";
   }
   return 0;
  }catch(Exception error){report["state"]=attempted?"owned_workplane_switch_outcome_unknown":"owned_workplane_switch_rejected_before_change";report["error"]=(error.InnerException??error).ToString();if(attempted&&journal!=null)try{native.Append(journal,"outcome_unknown",new Dictionary<string,object>{{"error",error.Message},{"noRecoveryOrReplay",true}});}catch(Exception append){report["journalError"]=append.Message;}return 1;
  }finally{report["finishedUtc"]=DateTime.UtcNow.ToString("o");report["copyFenced"]=File.Exists(fence)||Directory.Exists(fence);Freeze(Path.Combine(output,"result.json"),report);Console.WriteLine(Json.Serialize(new{state=report["state"],resultPath=Path.Combine(output,"result.json")}));}
 }

 static void ReadProof(Native native,Dictionary<string,object> before,Dictionary<string,object> fresh,DateTime returned){
  Need(Equals(fresh["state"],"passive_owned_workplane_observed")&&Equals(fresh["assemblySha256"],WorkerSha)&&Equals(fresh["diagnosticSha256"],ReaderSha),"Exact independent passive reader required.");
  foreach(string key in new[]{"cadMutationAttempted","selectionChangesAttempted","contextChangeAttempted","cadSaveAttempted","workerStarted"})Need(Equals(fresh[key],false),"Independent reader attempted a change.");
  DateTime readUtc=(DateTime)native.Call("EntitySets","Expiry",fresh["timestampUtc"]);Need(readUtc>returned&&readUtc<=DateTime.UtcNow.AddSeconds(5)&&DateTime.UtcNow-readUtc<TimeSpan.FromSeconds(300),"Independent read must be fresh and after the original return.");
  foreach(string side in new[]{"before","after"})Need(native.Digest(Map(before["observation"]))==native.Digest(Map(fresh[side])),"Independent WORLD observation differs.");
  var windows=Map(fresh["windowsAfter"]);var prior=Map(before["windows"]);var windowsBefore=Map(fresh["windowsBefore"]);foreach(var value in new[]{windows,prior,windowsBefore})value.Remove("timestampUtc");Need(Same(prior,windows)&&Same(windowsBefore,windows),"Independent window inventory differs.");
  Need(Near(Numbers(fresh["boundsWorldRaw"],6),Numbers(before["boundsFalseRaw"],6))&&Near(Numbers(fresh["boundsWorldRawAgain"],6),Numbers(before["boundsFalseRaw"],6))&&Equals(fresh["activeWorkplaneRaw"],0),"Independent bounds/world getter differs.");
  var frames=Rows(fresh["frames"]);Need(frames.Length==1&&Equals(frames[0]["compositeId"],before["targetCompositeId"])&&Near(Numbers(frames[0]["worldInfo"],9),Numbers(before["frameRaw"],9)),"Independent frame getter differs.");
 }
 static int Verify(string worker,string resultPath,string readPath,string output){
  var native=new Native(worker);var report=Load(resultPath);Need(Equals(report["state"],"owned_workplane_switch_executed_unverified")&&Equals(report["executionRequested"],true)&&Equals(report["cadMutationAttempted"],true)&&Equals(report["geometryMutationAttempted"],false)&&Equals(report["cadSaveAttempted"],false),"Original returned qualification receipt required.");
  string folder=(string)report["qualificationRoot"],operation=(string)report["operationId"],sha=(string)report["planSha256"];Need(SamePath(resultPath,Path.Combine(folder,"result.json"))&&SamePath((string)report["runtimeRoot"],native.Root),"Original qualification location differs.");
  Need(Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.Combine(native.Root,"..","..","evidence"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Original qualification must remain under project evidence.");native.Call("OwnedCopies","NoReparse",Path.GetFullPath(folder));native.Call("OwnedCopies","NoReparse",Path.GetFullPath(readPath));native.Call("OwnedCopies","NoReparse",Path.GetFullPath(output));
  Need(!File.Exists(output)&&!Directory.Exists(output)&&SamePath(Path.GetDirectoryName(Path.GetFullPath(output)),folder)&&SamePath(Path.GetDirectoryName(Path.GetFullPath(readPath)),folder),"New verifier output and independent read must be beside original result.");
  Need(Hash(Path.Combine(folder,"plan.json"))==sha,"Original plan changed.");var plan=Load(Path.Combine(folder,"plan.json"));Need(Equals(plan["operationId"],operation)&&Equals(plan["operation"],"qualification_existing_workplane_then_world")&&Equals(plan["copyId"],report["copyId"])&&Equals(plan["ownershipSha256"],report["ownershipSha256"])&&Equals(plan["sourceAuditSha256"],report["sourceAuditSha256"])&&Equals(plan["maximumSetterCalls"],2)&&Equals(plan["noAutomaticRecovery"],true)&&Same(plan["before"],report["before"]),"Original plan policy differs.");
  var pair=Map(report["result"]);Need(Equals(pair["setterCalls"],2),"Exactly two setters required.");
  foreach(var entry in new[]{new[]{"activation-return.json","activationReturnSha256"},new[]{"world-return.json","worldReturnSha256"}})Need(Hash(Path.Combine(folder,entry[0]))==(string)pair[entry[1]],"Original setter return changed.");
  var activated=Load(Path.Combine(folder,"activation-return.json"));var world=Load(Path.Combine(folder,"world-return.json"));var before=Map(report["before"]);
  Need(Equals(activated["targetCompositeId"],before["targetCompositeId"])&&Equals(activated["activeRaw"],before["targetCompositeId"])&&Equals(activated["setterReturned"],true)&&Equals(world["targetCompositeId"],0)&&Equals(world["activeRaw"],0)&&Equals(world["setterReturned"],true),"Original setter return proof differs.");
  CheckActive(before,Map(pair["activeObservation"]));CheckWorld(before,Map(pair["worldObservation"]));
  var fresh=Load(readPath);ReadProof(native,before,fresh,(DateTime)native.Call("EntitySets","Expiry",world["timestampUtc"]));
  string fence=Path.Combine(native.Root,"workingcopies",(string)report["copyId"],"mutation-fence.json");
  using(var serial=new FileStream(Path.Combine(native.Root,"operations","executor.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
  using(var lease=native.Hold((string)report["copyId"],(string)report["ownershipSha256"])){
   native.Recheck(lease);
   string working=(string)lease.GetType().GetField("WorkingPath",All).GetValue(lease);var model=Map(Map(before["observation"])["model"]);var activeWindow=Map(Map(before["windows"])["activeWindow"]);Need(SamePath((string)model["path"],working)&&SamePath((string)activeWindow["modelPath"],working)&&Equals(activeWindow["modelId"],model["id"]),"Verified evidence is not the leased owned working document.");
   foreach(var item in Map(report["protectedHashes"]))Need(Hash(item.Key)==(string)item.Value,"Protected file changed before independent verification.");
   Need(Hash((string)report["claimPath"])==(string)report["claimSha256"],"Original one-use claim changed.");var claim=Load((string)report["claimPath"]);Need(claim.Count==5&&Equals(claim["operationId"],operation)&&Equals(claim["planSha256"],sha)&&Equals(claim["approvalSha256"],plan["approvalSha256"])&&Equals(claim["automaticReplayForbidden"],true)&&SamePath((string)claim["qualificationRoot"],folder),"Claim policy differs.");
   Need(Hash(fence)==(string)report["executionFenceSha256"],"Foreign or modified fence retained.");var f=Load(fence);Need(f.Count==6&&Equals(f["schemaVersion"],1)&&Equals(f["operationId"],operation)&&Equals(f["planSha256"],sha)&&Equals(f["copyId"],report["copyId"])&&Equals(f["ownershipSha256"],report["ownershipSha256"])&&Equals(f["automaticReplayForbidden"],true),"Fence policy differs.");
   object journal=native.Resume(folder,operation,sha);var type=journal.GetType();Need(Equals(type.GetProperty("State",All).GetValue(journal,null),"executed_unverified"),"Original journal is not pending verification.");
   var returned=(Dictionary<string,object>)type.GetField("CommandReturnedDetails",All).GetValue(journal);Need(returned.Count==5&&Equals(returned["nativeRequestId"],operation)&&Equals(returned["activationReturnSha256"],pair["activationReturnSha256"])&&Equals(returned["worldReturnSha256"],pair["worldReturnSha256"])&&Equals(returned["worldActivationReturned"],true)&&Equals(returned["setterCalls"],2),"Original authoritative return differs.");
   var evidence=new[]{new Dictionary<string,object>{{"requestId",operation},{"sha256",Hash(resultPath)}},new Dictionary<string,object>{{"requestId",operation},{"sha256",Hash(readPath)}}};
   native.Append(journal,"succeeded_verified",new Dictionary<string,object>{{"evidence",evidence},{"noCommandReplay",true},{"qualificationOnly",true}});
   native.Recheck(lease);
   Need(Hash(fence)==(string)report["executionFenceSha256"],"Fence changed during verification.");File.Delete(fence);
   Freeze(output,new{state="owned_workplane_switch_qualification_verified",operationId=operation,resultSha256=Hash(resultPath),independentReadSha256=Hash(readPath),recordedJournalState="succeeded_verified",matchingCopyFenceReleased=true,noCommandReplay=true,protectedHashChecks=Map(report["protectedHashes"]).Count,scope="one existing workplane then WORLD; native API qualification only, not a published MCP tool or saved active-context claim",timestampUtc=DateTime.UtcNow.ToString("o")});
  }
  Console.WriteLine("PASS: independent file verification; no CAD access or command replay.");return 0;
 }

 sealed class Fake:IContext {
  readonly Dictionary<string,object> snapshot,windows;readonly double[] frame,bounds;readonly int target,solid;readonly string fault;
  internal readonly List<int> Calls=new List<int>();int active;
  internal Fake(Dictionary<string,object> source,string failure){snapshot=Map(source["after"]);windows=Map(source["windowsAfter"]);var f=Rows(source["frames"]).Single();target=(int)f["compositeId"];solid=(int)source["bodyCompositeId"];frame=Numbers(f["worldInfo"],9);bounds=Numbers(source["boundsWorldRaw"],6);fault=failure;}
  public Dictionary<string,object> Observation(){var value=Map(snapshot);value["contextBefore"]=value["contextAfter"]=new[]{0,active};value["timestampUtc"]=DateTime.UtcNow.ToString("o");
   if(active!=0){var entities=Rows(value["entities"]);var body=entities.Single(v=>Equals(v["nativeTypeCode"],40));var geometry=Map(body["geometry"]);geometry["coordinateFrame"]="active_workplane_unqualified";if(fault!="world_coordinates")geometry["cogRaw"]=new[]{2,6,-3};body["geometry"]=geometry;value["entities"]=entities;
    if(fault!="world_coordinates"){var planes=Rows(value["workplanes"]);planes[0]["originRaw"]=new[]{0,0,0};planes[0]["xaxisRaw"]=new[]{1,0,0};planes[0]["yaxisRaw"]=new[]{0,1,0};planes[0]["zaxisRaw"]=new[]{0,0,1};value["workplanes"]=planes;}
    if(fault=="selection_changed")value["selectionBefore"]=value["selectionAfter"]=new[]{1};
   }return value;}
  public Dictionary<string,object> Windows(){return Map(windows);}
  public void Confirm(){if(fault=="foreign_document"&&Calls.Count>0)throw new IOException("Injected document switch.");}
  public int Active(){return active;}
  public int Composite(int type,int native){return type==54?target:solid;}
  public object WorkplaneIds(){return new[]{target};}
  public double[] Frame(int composite){return active!=0&&fault=="local_frame_getter"?new double[]{0,0,0,0,0,1,0,1,0}:frame.ToArray();}
  public double[] Bounds(int composite){var value=bounds.ToArray();if(active!=0&&fault=="body_changed")value[1]+=1;return value;}
  public void Activate(int composite){Calls.Add(composite);active=composite;if(fault=="wrong_active"&&composite!=0)active=123;if(fault=="throw_after_first"&&composite!=0||fault=="throw_after_world"&&composite==0)throw new IOException("Injected exception after setter effect.");}
  public void Dispose(){}
 }
 static int SelfTest(string source){
  var seed=Load(source);Need(Equals(seed["state"],"passive_owned_workplane_observed")&&Near(Numbers(Rows(seed["frames"]).Single()["worldInfo"],9),new double[]{2,3,4,0,1,0,0,0,1}),"Expected offline fixture frame required.");int total=0;
  foreach(string fault in new[]{"active_coordinates","world_coordinates","local_frame_getter","throw_after_first","wrong_active","body_changed","foreign_document","selection_changed","throw_after_world"}){
   using(var context=new Fake(seed,fault)){
    var before=Capture(context);var receipts=new List<string>();bool passed=false;
    try{var result=Pair(context,before,(name,value)=>{receipts.Add(name);return new string('a',64);},()=>{});Need(Equals(result["setterCalls"],2),"Control lost setter count.");passed=true;}catch(InvalidDataException){}catch(IOException){}
    bool success=new[]{"active_coordinates","world_coordinates","local_frame_getter"}.Contains(fault);Need(passed==success,"Unexpected fault outcome: "+fault);
    Need(context.Calls.Count==(success||fault=="throw_after_world"?2:1),"Unexpected automatic WORLD call or retry after fault: "+fault);
    Need(receipts.Count==(success?2:fault=="throw_after_first"||fault=="wrong_active"?0:1),"Unproven return receipt: "+fault);total++;
   }
  }
  Need(Near(Local(new double[]{0,0,10},new double[]{2,3,4,0,1,0,0,0,1},true),new double[]{2,6,-3}),"Independent expected COG mapping failed.");
  Console.WriteLine(Json.Serialize(new{state="passed",tests=total,powerShapeConnectionAttempted=false,cadMutationAttempted=false,scope="coordinate frame semantics and no WORLD/replay after unknown, changed selection, geometry or document"}));return 0;
 }
 static void Repath(object value,object id,string working){
  var map=value as Dictionary<string,object>;if(map!=null){if(map.ContainsKey("modelId")&&Equals(map["modelId"],id)){if(map.ContainsKey("modelPath"))map["modelPath"]=working;if(map.ContainsKey("modelName"))map["modelName"]=Path.GetFileNameWithoutExtension(working);}foreach(var item in map.Values.ToArray())Repath(item,id,working);}
  else if(value is IEnumerable&&!(value is string))foreach(var item in Items(value))Repath(item,id,working);
 }
 static int SelfTestFiles(string worker,string source,string directory){
  var native=new Native(worker);Need(native.Root.StartsWith(Path.GetFullPath(Path.Combine(native.Root,"..","..","work"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"File fault tests require a worker copy under project work, never the installed runtime.");
  directory=Path.GetFullPath(directory);Need(!Directory.Exists(directory)&&!File.Exists(directory),"New file-test evidence required.");Directory.CreateDirectory(directory);Directory.CreateDirectory(Path.Combine(native.Root,"operations"));var controls=new List<object>();
  foreach(string fault in new[]{"control","wrong_frame","foreign_fence"}){
   string folder=Path.Combine(directory,fault);Directory.CreateDirectory(folder);string id=Guid.NewGuid().ToString("N"),copy=Guid.NewGuid().ToString("N");string input=Path.Combine(native.Root,"synthetic",copy+".psmodel");Freeze(input,new{syntheticOnly=true,notACadModel=true});
   var created=Map(native.Call("OwnedCopies","Create",native.Root,copy,input,Hash(input),2));string working=(string)Map(Map(created["ownership"])["working"])["path"],owner=(string)created["ownershipSha256"];
   var seed=Load(source);var obs=Map(seed["after"]);var model=Map(obs["model"]);object modelId=model["id"];model["path"]=working;model["name"]=Path.GetFileNameWithoutExtension(working);obs["model"]=model;seed["after"]=obs;var windows=Map(seed["windowsAfter"]);Repath(windows,modelId,working);seed["windowsAfter"]=windows;
   Dictionary<string,object> before,pair;
   using(var fake=new Fake(seed,"active_coordinates")){before=Capture(fake);pair=Pair(fake,before,(name,value)=>Freeze(Path.Combine(folder,name),value),()=>{});}
   var plan=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"operation","qualification_existing_workplane_then_world"},{"copyId",copy},{"ownershipSha256",owner},{"before",before},{"sourceAuditSha256",new string('a',64)},{"approvalSha256",new string('b',64)},{"maximumSetterCalls",2},{"noAutomaticRecovery",true}};
   string planSha=Freeze(Path.Combine(folder,"plan.json"),plan),fence=Path.Combine(native.Root,"workingcopies",copy,"mutation-fence.json");
   string fenceSha=Freeze(fence,new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",planSha},{"copyId",copy},{"ownershipSha256",owner},{"automaticReplayForbidden",true}});
   string claim=Path.Combine(folder,"claim.once");string claimSha=Freeze(claim,new{operationId=id,planSha256=planSha,qualificationRoot=folder,approvalSha256=new string('b',64),automaticReplayForbidden=true});
   var journal=native.Journal(folder,id,planSha);native.Append(journal,"prepared",new Dictionary<string,object>());native.Append(journal,"execution_claimed",new Dictionary<string,object>());native.Append(journal,"command_started",new Dictionary<string,object>());
   native.Append(journal,"command_returned",new Dictionary<string,object>{{"nativeRequestId",id},{"activationReturnSha256",pair["activationReturnSha256"]},{"worldReturnSha256",pair["worldReturnSha256"]},{"worldActivationReturned",true},{"setterCalls",2}});native.Append(journal,"executed_unverified",new Dictionary<string,object>());
   var protectedFiles=new Dictionary<string,object>();foreach(string path in new[]{input,working,Path.Combine(native.Root,"workingcopies",copy,"checkpoint","Before.psmodel"),Path.Combine(native.Root,"workingcopies",copy,"ownership.json")})protectedFiles[path]=Hash(path);
   var result=new Dictionary<string,object>{{"state","owned_workplane_switch_executed_unverified"},{"executionRequested",true},{"cadMutationAttempted",true},{"geometryMutationAttempted",false},{"cadSaveAttempted",false},{"qualificationRoot",folder},{"runtimeRoot",native.Root},{"operationId",id},{"planSha256",planSha},{"copyId",copy},{"ownershipSha256",owner},{"sourceAuditSha256",new string('a',64)},{"before",before},{"result",pair},{"protectedHashes",protectedFiles},{"claimPath",claim},{"claimSha256",claimSha},{"executionFenceSha256",fenceSha}};
   result["workerSha256"]=WorkerSha;string resultPath=Path.Combine(folder,"result.json");Freeze(resultPath,result);
   DateTime returned=(DateTime)native.Call("EntitySets","Expiry",Load(Path.Combine(folder,"world-return.json"))["timestampUtc"]);var deadline=DateTime.UtcNow.AddSeconds(1);while(DateTime.UtcNow<=returned&&DateTime.UtcNow<deadline)System.Threading.Thread.Sleep(1);Need(DateTime.UtcNow>returned,"Clock did not advance after synthetic return.");
   var fresh=Map(seed);fresh["before"]=fresh["after"]=Map(before["observation"]);fresh["windowsBefore"]=fresh["windowsAfter"]=Map(before["windows"]);fresh["timestampUtc"]=DateTime.UtcNow.ToString("o");fresh["assemblySha256"]=WorkerSha;fresh["diagnosticSha256"]=ReaderSha;
   if(fault=="wrong_frame"){var frames=Rows(fresh["frames"]);var frame=Numbers(frames[0]["worldInfo"],9);frame[0]+=1;frames[0]["worldInfo"]=frame;fresh["frames"]=frames;}
   string readPath=Path.Combine(folder,"independent-native-result.json");Freeze(readPath,fresh);
   if(fault=="foreign_fence")File.WriteAllText(fence,"{\"foreign\":true}");string heldFence=Hash(fence);bool passed=false;string error=null;
   try{Verify(worker,resultPath,readPath,Path.Combine(folder,"verification.json"));passed=true;}catch(Exception failure){error=failure.Message;}
   Need(passed==(fault=="control"),"Unexpected independent verifier result: "+fault);if(passed)Need(!File.Exists(fence)&&Directory.GetFiles(Path.Combine(folder,"operations",id),"*.json").Length==6,"Control failed to release only its fence after success.");else Need(Hash(fence)==heldFence&&Directory.GetFiles(Path.Combine(folder,"operations",id),"*.json").Length==5&&!File.Exists(Path.Combine(folder,"verification.json")),"Rejected verifier altered fence/journal.");
   controls.Add(new{fault,passed,expectedRejection=fault!="control",error});
  }
  Freeze(Path.Combine(directory,"audit.json"),new{state="passed",tests=3,powerShapeConnectionAttempted=false,cadMutationAttempted=false,scope="synthetic owned files and journals only; exact native worker; no COM context created",controls});Console.WriteLine("PASS: three independent verifier file controls; no CAD connection.");return 0;
 }
}
