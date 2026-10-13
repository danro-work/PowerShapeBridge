using System;
using System.Collections.Generic;
using System.IO;

namespace PowerShapeBridge {
 internal static class OwnedSaveTargetTests {
  internal static int Run() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Owned SaveAs target test failed at "+count);count++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}catch(InvalidOperationException){rejected=true;}check(rejected);};
   var fixture=new OwnedOperationPlanTests.Fixture(true);
   string pinFolder=Path.Combine(fixture.Root,"empty-pinned-directory");Directory.CreateDirectory(pinFolder);
   using(var directory=new OwnedDirectoryLease(pinFolder)){rejects(()=>Directory.Move(pinFolder,pinFolder+"-moved"));directory.Recheck();count++;}
   // Program.Root is AppDomain.BaseDirectory, which carries a trailing separator.
   // Equivalent spellings must retain the same directory identity and rename guard.
   using(var directory=new OwnedDirectoryLease(pinFolder+"\\"))using(var ordinary=new OwnedDirectoryLease(pinFolder)) {
    check(EntitySets.Canonical(directory.Identity)==EntitySets.Canonical(ordinary.Identity));
    rejects(()=>Directory.Move(pinFolder,pinFolder+"-moved"));directory.Recheck();count++;
   }
   using(var directory=new OwnedDirectoryLease(pinFolder.Replace('\\','/')+"/")) {directory.Recheck();count++;}
   rejects(()=>new OwnedDirectoryLease(pinFolder+"\\..\\empty-pinned-directory\\"));
   var parameters=new Dictionary<string,object>{{"bindingId",fixture.BindingId},{"bindingSha256",fixture.Parameters["bindingSha256"]}};
   string trailingId=Guid.NewGuid().ToString("N");
   var trailing=OwnedSaveTargets.Prepare(fixture.Root+"\\",trailingId,parameters);
   using(var trailingLease=new OwnedSaveTargets.TargetLease(fixture.Root+"\\",trailingId,(string)trailing["targetSha256"])) {trailingLease.Recheck();count++;}
   string id=Guid.NewGuid().ToString("N");var result=OwnedSaveTargets.Prepare(fixture.Root,id,parameters);
   var plan=(Dictionary<string,object>)result["target"];string sha=(string)result["targetSha256"];
   string folder=OwnedSaveTargets.Folder(fixture.Root,id),target=(string)plan["resultPath"],copyId=(string)plan["copyId"];
   string sourceSha=Program.Hash(fixture.Working),checkpointSha=Program.Hash((string)plan["checkpointPath"]);
   check(Equals(result["state"],"owned_save_target_prepared_files")&&Equals(result["cadSaveAttempted"],false)&&Equals(plan["executionEnabled"],false));
   check(!File.Exists(target)&&Path.GetFileName(target)=="Result_"+id+".psmodel"&&!Directory.Exists(Path.Combine(fixture.Root,"operations",id)));
   check(File.Exists(folder+".reservation.json")&&Program.Hash(Path.Combine(folder,"target.json"))==sha);
   rejects(()=>OwnedSaveTargets.Prepare(fixture.Root,id,parameters));
   rejects(()=>new OwnedSaveTargets.TargetLease(fixture.Root,id,new string('f',64)));
   using(var lease=new OwnedSaveTargets.TargetLease(fixture.Root,id,sha)) {
    check(lease.Copy.WorkingPath==fixture.Working);lease.Recheck();count++;
    rejects(()=>Directory.Move(folder,folder+"-moved"));
    rejects(()=>File.AppendAllText(Path.Combine(folder,"target.json"),"tamper"));
    rejects(()=>File.AppendAllText(Path.Combine(fixture.Root,"documentbindings",fixture.BindingId+".json"),"tamper"));
    File.WriteAllText(target,"Opaque synthetic collision; never a CAD file.");rejects(()=>lease.Recheck());File.Delete(target);
    Directory.CreateDirectory(target);rejects(()=>lease.Recheck());Directory.Delete(target);
    string claim=Path.Combine(folder,"save-once.json");File.WriteAllText(claim,"synthetic prior claim");rejects(()=>lease.Recheck());File.Delete(claim);
    string fence=OwnedTranslation.FencePath(fixture.Root,copyId);File.WriteAllText(fence,"synthetic unknown mutation");rejects(()=>lease.Recheck());File.Delete(fence);
    lease.Recheck();count++;
   }
   check(Program.Hash(fixture.Working)==sourceSha&&Program.Hash((string)plan["checkpointPath"])==checkpointSha);
   string targetBefore=Program.Hash(Path.Combine(folder,"target.json"));
   File.AppendAllText(Path.Combine(folder,"target.json")," ");rejects(()=>new OwnedSaveTargets.TargetLease(fixture.Root,id,sha));
   check(Program.Hash(Path.Combine(folder,"target.json"))!=targetBefore);
   string replacedId=Guid.NewGuid().ToString("N");var replaced=OwnedSaveTargets.Prepare(fixture.Root,replacedId,parameters);string replacedFolder=OwnedSaveTargets.Folder(fixture.Root,replacedId);
   Directory.Move(replacedFolder,replacedFolder+"-old");Directory.CreateDirectory(replacedFolder);File.Copy(Path.Combine(replacedFolder+"-old","target.json"),Path.Combine(replacedFolder,"target.json"));
   rejects(()=>new OwnedSaveTargets.TargetLease(fixture.Root,replacedId,(string)replaced["targetSha256"]));
   string compatibleId=Guid.NewGuid().ToString("N");var compatible=OwnedSaveTargets.Prepare(fixture.Root,compatibleId,parameters);
   NativeJournal.Freeze(Path.Combine(OwnedSaveTargets.Folder(fixture.Root,compatibleId),"prepared-receipt.json"),compatible);
   using(var compatibleLease=new OwnedSaveTargets.TargetLease(fixture.Root,compatibleId,(string)compatible["targetSha256"])) {compatibleLease.Recheck();count++;}
   var extra=new Dictionary<string,object>(parameters){{"resultPath",fixture.Working}};
   string extraId=Guid.NewGuid().ToString("N");rejects(()=>OwnedSaveTargets.Prepare(fixture.Root,extraId,extra));check(!Directory.Exists(OwnedSaveTargets.Folder(fixture.Root,extraId)));
   rejects(()=>OwnedSaveTargets.Folder(fixture.Root,"../other"));
   string busyId=Guid.NewGuid().ToString("N"),busyFolder=OwnedSaveTargets.Folder(fixture.Root,busyId);
   File.WriteAllText(busyFolder+".reservation.json","synthetic interrupted reservation");rejects(()=>OwnedSaveTargets.Prepare(fixture.Root,busyId,parameters));check(!Directory.Exists(busyFolder));
   string copyFence=OwnedTranslation.FencePath(fixture.Root,copyId);File.WriteAllText(copyFence,"synthetic unresolved outcome");
   string fencedId=Guid.NewGuid().ToString("N");rejects(()=>OwnedSaveTargets.Prepare(fixture.Root,fencedId,parameters));check(!Directory.Exists(OwnedSaveTargets.Folder(fixture.Root,fencedId)));File.Delete(copyFence);
   return count;
  }
 }
}
