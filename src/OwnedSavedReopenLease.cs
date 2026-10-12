using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerShapeBridge {
 // Holds immutable provenance, physical directories, seed and copy dependencies.
 // Historical mode is ONLY for independent verification, never OPEN admission.
 internal sealed class OwnedSavedReopenLease : IDisposable {
  internal readonly Dictionary<string,object> Plan;
  internal readonly OwnedSavedSource Source;
  internal readonly OwnedCopies.OwnershipLease Copy;
  internal readonly DateTime Expires;
  readonly bool historical;readonly string seedPath;
  readonly OwnedOperationPlans.Proofs proofs=new OwnedOperationPlans.Proofs();
  readonly List<OwnedDirectoryLease> directories=new List<OwnedDirectoryLease>();
  FileStream seed;
  internal static void Validate(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("reopenId")||!EntitySets.Hex(args["reopenId"],32)||!args.ContainsKey("reopenSha256")||!EntitySets.Hex(args["reopenSha256"],64))throw new ArgumentException("Invalid saved reopen identity.");
  }
  internal OwnedSavedReopenLease(string root,Dictionary<string,object> args,bool historicalProof=false) {
   historical=historicalProof;
   try {
    Validate(args);root=OwnedCopies.LocalPath(root.TrimEnd('\\','/'));string id=(string)args["reopenId"],folder=Path.Combine(root,"savedreopens",id);
    foreach(string path in new[]{root,Path.Combine(root,"savedreopens"),folder,Path.Combine(root,"workingcopies"),Path.Combine(root,"workingcopies",id),Path.Combine(root,"workingcopies",id,"checkpoint")})directories.Add(new OwnedDirectoryLease(path));
    Plan=proofs.Json(Path.Combine(folder,"reopen.json"),(string)args["reopenSha256"]);
    string[] fields={"schemaVersion","reopenId","operation","bridgeVersion","preparedUtc","expiresUtc","executionEnabled","verificationRequestId","verificationReceiptSha256","saveOperationId","targetId","targetSha256","savedResult","seed","copyId","ownershipSha256","workingPath","workingSha256","checkpointPath","checkpointSha256","directoryIdentity"};
    if(Plan.Count!=fields.Length||fields.Any(k=>!Plan.ContainsKey(k))||!Equals(Plan["schemaVersion"],1)||!Equals(Plan["reopenId"],id)||!Equals(Plan["copyId"],id)||!Equals(Plan["operation"],"reopen_owned_saved_result")||!Equals(Plan["executionEnabled"],false))throw new IOException("Saved reopen plan differs.");
    var sourceArgs=new Dictionary<string,object>{{"verificationRequestId",Plan["verificationRequestId"]},{"verificationReceiptSha256",Plan["verificationReceiptSha256"]}};
    Source=new OwnedSavedSource(root,sourceArgs,historical);
    foreach(string field in new[]{"saveOperationId","targetId","targetSha256","savedResult"})if(EntitySets.Canonical(Plan[field])!=EntitySets.Canonical(Source.Verified[field]))throw new IOException("Saved reopen provenance differs.");
    DateTime prepared=EntitySets.Expiry(Plan["preparedUtc"]);Expires=EntitySets.Expiry(Plan["expiresUtc"]);
    if(prepared>DateTime.UtcNow.AddSeconds(5)||prepared<EntitySets.Expiry(Source.Verified["timestampUtc"])||Expires!=Source.Expires||prepared>=Expires)throw new IOException("Saved reopen original lifetime differs.");
    var reservation=proofs.Json(folder+".reservation.json",null);
    if(reservation.Count!=5||!Equals(reservation["schemaVersion"],1)||!Equals(reservation["reopenId"],id)||!Equals(reservation["automaticReplayForbidden"],true)||sourceArgs.Any(p=>!Equals(reservation[p.Key],p.Value)))throw new IOException("Saved reopen reservation differs.");
    if(EntitySets.Canonical(directories[2].Identity)!=EntitySets.Canonical(Plan["directoryIdentity"]))throw new IOException("Prepared saved reopen directory replaced.");
    var record=Plan["seed"] as Dictionary<string,object>;seedPath=Path.Combine(folder,"DiskReopen_"+id+".psmodel");
    if(record==null||record.Count!=4||!Equals(record["path"],seedPath)||!Equals(record["sha256"],Source.Saved["sha256"])||!Equals(Convert.ToInt64(record["length"]),Convert.ToInt64(Source.Saved["length"])))throw new IOException("Saved reopen seed differs.");
    seed=new FileStream(seedPath,FileMode.Open,FileAccess.Read,FileShare.Read);
    Copy=OwnedCopies.Hold(root,id,(string)Plan["ownershipSha256"]);
    var ownership=(Dictionary<string,object>)Copy.Inspection["ownership"];
    if(!Equals(ownership["schemaVersion"],2)||EntitySets.Canonical(ownership["source"])!=EntitySets.Canonical(record)||!Equals(Plan["workingPath"],Copy.WorkingPath)||!Equals(Plan["checkpointPath"],Copy.CheckpointPath)||
       !Equals(Plan["workingSha256"],Source.Saved["sha256"])||!Equals(Plan["checkpointSha256"],Source.Saved["sha256"])||!Equals(Copy.Inspection["workingSha256"],Source.Saved["sha256"])||!Equals(Copy.CheckpointSha,Source.Saved["sha256"]))throw new IOException("Saved reopen owned files differ.");
    var records=new[]{record,(Dictionary<string,object>)ownership["working"],(Dictionary<string,object>)ownership["checkpoint"],Source.Saved};
    if(records.Select(r=>{var identity=(Dictionary<string,object>)r["fileIdentity"];return identity["volumeSerial"]+":"+identity["fileIndex"];}).Distinct(StringComparer.Ordinal).Count()!=4)throw new IOException("Saved reopen files alias.");
    Recheck();
   }catch{Dispose();throw;}
  }
  internal void Recheck() {
   Source.Recheck();Copy.Recheck();foreach(var pin in directories)pin.Recheck();
   if(!historical&&DateTime.UtcNow>=Expires)throw new InvalidOperationException("Saved reopen intent expired.");
   var record=(Dictionary<string,object>)Plan["seed"];OwnedCopies.NoReparse(seedPath);
   if((File.GetAttributes(seedPath)&FileAttributes.ReadOnly)==0||seed.Length!=Convert.ToInt64(record["length"])||EntitySets.Canonical(OwnedCopies.FileIdentity(seed,seedPath,true))!=EntitySets.Canonical(record["fileIdentity"])||
      OwnedCopies.Digest(seed)!=(string)record["sha256"]||OwnedCopies.Digest(seed)!=(string)record["sha256"]||OwnedCopies.Digest(Copy.Work)!=(string)Plan["workingSha256"])throw new IOException("Saved reopen seed/working identity or bytes changed.");
  }
  public void Dispose(){if(seed!=null)seed.Dispose();if(Copy!=null)Copy.Dispose();if(Source!=null)Source.Dispose();proofs.Dispose();foreach(var pin in directories)pin.Dispose();}
 }
}
