using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace PowerShapeBridge {
 internal static class CheckpointRecoveryTests {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLink(string newName,string existingName,IntPtr security);
  static void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
  static Dictionary<string,string> Hashes(string root){return Directory.GetFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,path=>Program.Hash(path));}
  static Dictionary<string,object> Copy(Dictionary<string,object> value){return Program.Json.Deserialize<Dictionary<string,object>>(Program.Json.Serialize(value));}
  internal static int Run() {
   if(!File.Exists(Path.Combine(Program.Root,".checkpoint-recovery-fixture")))throw new InvalidOperationException("Explicit isolated checkpoint recovery stage required; no CAD.");
   var rows=new List<Dictionary<string,object>>();
   foreach(string fault in new[]{"prepared_only","control","working_changed_before","expired","future_intent","checkpoint_writable","checkpoint_changed","checkpoint_replaced","checkpoint_hardlink","ownership_changed","plan_changed","busy_executor","busy_checkpoint","partial_after_claim","partial_after_copy","foreign_copy_identity"}) {
    string root=Path.Combine(Program.Root,"work","checkpoint-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"operations"));Directory.CreateDirectory(Path.Combine(root,"responses"));
    string source=Path.Combine(root,"Source.psmodel");File.WriteAllText(source,"Opaque checkpoint saved bytes; never CAD.");var copy=OwnedCopies.Create(root,Guid.NewGuid().ToString("N"),source,Program.Hash(source));var owner=(Dictionary<string,object>)copy["ownership"];
    string working=(string)((Dictionary<string,object>)owner["working"])["path"],checkpoint=(string)((Dictionary<string,object>)owner["checkpoint"])["path"];
    string originId=Guid.NewGuid().ToString("N"),intentId=Guid.NewGuid().ToString("N"),newId=Guid.NewGuid().ToString("N");
    Directory.CreateDirectory(Path.Combine(root,"copyopenplans"));var origin=new Dictionary<string,object>{{"schemaVersion",1},{"operationId",originId},{"operation","open_working_copy"},{"copyId",copy["copyId"]},{"ownershipSha256",copy["ownershipSha256"]}};
    string originSha=NativeJournal.Freeze(Path.Combine(root,"copyopenplans",originId+".json"),origin);
    var journal=new NativeJournal(root,originId,originSha);journal.Append("prepared",new Dictionary<string,object>());journal.Append("execution_claimed",new Dictionary<string,object>());journal.Append("command_started",new Dictionary<string,object>());
    NativeJournal.Freeze(Path.Combine(Path.GetDirectoryName(working),"open-once.json"),new Dictionary<string,object>{{"operationId",originId},{"planSha256",originSha},{"copyId",copy["copyId"]},{"ownershipSha256",copy["ownershipSha256"]},{"automaticReplayForbidden",true}});
    var arguments=new Dictionary<string,object>{{"operationId",originId},{"operationPlanSha256",originSha}};
    var prepared=CheckpointRecovery.Prepare(root,intentId,arguments);prepared["bridgeRequestId"]=intentId;
    NativeJournal.Freeze(Path.Combine(root,"responses",intentId+".json"),prepared);
    if(fault=="prepared_only") {
     File.Copy(Path.Combine(Program.Root,"bridge.config.json"),Path.Combine(root,"bridge.config.json"));
     File.Copy(Path.Combine(Program.Root,"PSBridge.Worker.exe"),Path.Combine(root,"PSBridge.Worker.exe"));
     rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"originId",originId},{"originPlanSha256",originSha},{"prepared",prepared},{"artifactHashes",Hashes(root)},{"cadConnectionAttempted",false}});continue;
    }
    if(fault=="working_changed_before")File.AppendAllText(working," Own saved working file changed; checkpoint retained.");
    var originalProtected=new[]{source,checkpoint,working,Path.Combine(root,"copyopenplans",originId+".json"),Path.Combine(Path.GetDirectoryName(working),"ownership.json"),Path.Combine(Path.GetDirectoryName(working),"open-once.json")}.Concat(Directory.GetFiles(Path.Combine(root,"operations",originId),"*.json")).ToDictionary(path=>path,path=>Program.Hash(path));
    string intentSha=(string)prepared["recoverySha256"];string intentPath=Path.Combine(root,"checkpointrecoveries",intentId+".json");FileStream busy=null;
    if(fault=="expired"||fault=="future_intent") {
     var altered=Copy((Dictionary<string,object>)prepared["recovery"]);altered["preparedUtc"]=(fault=="expired"?DateTime.UtcNow.AddSeconds(-310):DateTime.UtcNow.AddHours(1)).ToString("o");altered["expiresUtc"]=(fault=="expired"?DateTime.UtcNow.AddSeconds(-10):DateTime.UtcNow.AddHours(1).AddSeconds(300)).ToString("o");
     File.WriteAllText(intentPath,Program.Json.Serialize(altered));intentSha=Program.Hash(intentPath);
    }
    if(fault=="checkpoint_writable")File.SetAttributes(checkpoint,FileAttributes.Normal);
    if(fault=="checkpoint_changed"){File.SetAttributes(checkpoint,FileAttributes.Normal);File.AppendAllText(checkpoint," Own injected corruption.");File.SetAttributes(checkpoint,FileAttributes.ReadOnly);originalProtected[checkpoint]=Program.Hash(checkpoint);}
    if(fault=="checkpoint_replaced"){var bytes=File.ReadAllBytes(checkpoint);File.SetAttributes(checkpoint,FileAttributes.Normal);File.Move(checkpoint,Path.Combine(root,"OldCheckpoint.psmodel"));File.WriteAllBytes(checkpoint,bytes);File.SetAttributes(checkpoint,FileAttributes.ReadOnly);}
    if(fault=="checkpoint_hardlink")Require(CreateHardLink(Path.Combine(root,"Alias.psmodel"),checkpoint,IntPtr.Zero),"Hardlink negative fixture creation failed.");
    if(fault=="ownership_changed"){string path=Path.Combine(Path.GetDirectoryName(working),"ownership.json");File.AppendAllText(path," ");originalProtected[path]=Program.Hash(path);}
    if(fault=="plan_changed"){string path=Path.Combine(root,"copyopenplans",originId+".json");File.AppendAllText(path," ");originalProtected[path]=Program.Hash(path);}
    if(fault=="busy_executor")busy=new FileStream(Path.Combine(root,"operations","executor.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
    if(fault=="busy_checkpoint")busy=new FileStream(checkpoint,FileMode.Open,FileAccess.Read,FileShare.None);
    if(fault=="foreign_copy_identity")newId=(string)copy["copyId"];
    var parameters=new Dictionary<string,object>{{"recoveryId",intentId},{"recoverySha256",intentSha}};Dictionary<string,object> result;
    try{result=CheckpointRecovery.Create(root,newId,parameters,phase=>{if(fault=="partial_"+phase)throw new IOException("Synthetic interrupted recovery "+phase);});}finally{if(busy!=null)busy.Dispose();}
    result["bridgeRequestId"]=newId;NativeJournal.Freeze(Path.Combine(root,"responses",newId+".json"),result);
    bool control=fault=="control"||fault=="working_changed_before",partial=fault=="partial_after_claim"||fault=="partial_after_copy";
    Require(Equals(result["state"],control?"checkpoint_working_copy_prepared_disk":partial?"checkpoint_copy_outcome_unknown":"checkpoint_copy_rejected_before_creation"),fault+": wrong state "+Program.Json.Serialize(result));
    Require(originalProtected.All(pair=>Program.Hash(pair.Key)==pair.Value),fault+": original artifacts modified.");
    string claim=Path.Combine(root,"checkpointrecoveries",intentId+".copy-once.json");Require(File.Exists(claim)==(control||partial),fault+": claim phase differs.");
    if(control) {
     var recovered=(Dictionary<string,object>)result["ownership"];string work=(string)((Dictionary<string,object>)recovered["working"])["path"],before=(string)((Dictionary<string,object>)recovered["checkpoint"])["path"];
     Require(Program.Hash(work)==Program.Hash(checkpoint)&&Program.Hash(before)==Program.Hash(checkpoint)&&(File.GetAttributes(before)&FileAttributes.ReadOnly)!=0,"Recovered bytes/readonly differ.");
     Require(EntitySets.Canonical(recovered["source"])==EntitySets.Canonical(((Dictionary<string,object>)prepared["recovery"])["checkpoint"]),"Recovered source is not protected checkpoint.");
    }
    // Only test duplicate claimed intents. No automatic retry of a transient failure.
    if(control||partial){var beforeRetry=Hashes(root);var duplicate=CheckpointRecovery.Create(root,Guid.NewGuid().ToString("N"),parameters);Require(Equals(duplicate["state"],"checkpoint_copy_rejected_before_creation"),fault+": duplicate intent accepted.");var afterRetry=Hashes(root);Require(beforeRetry.Count==afterRetry.Count&&beforeRetry.All(pair=>afterRetry[pair.Key]==pair.Value),"Duplicate intent changed artifacts.");}
    rows.Add(new Dictionary<string,object>{{"fault",fault},{"root",root},{"originId",originId},{"prepared",prepared},{"parameters",parameters},{"result",result},{"copyId",newId},{"originPlanSha256",originSha},{"protectedHashes",originalProtected},{"artifactHashes",Hashes(root)},{"cadConnectionAttempted",false}});
   }
   Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","checkpoint-recovery-matrix.json"),new Dictionary<string,object>{{"schema","psbridge.checkpoint-recovery-matrix.v1"},{"state","passed"},{"rows",rows},{"cadConnectionAttempted",false},{"scope","Filesystem-only protected checkpoint recovery; no OPEN, CAD or original outcome resolution"}});
   Console.WriteLine("PASS: "+rows.Count+" native checkpoint recovery cases; no CAD.");return rows.Count;
  }
 }
}
