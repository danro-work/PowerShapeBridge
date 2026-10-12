using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PowerShapeBridge {
 // Native append-only counterpart of the local journal schema; no command execution.
 internal sealed class NativeJournal {
  readonly string folder,id,planSha;string previous,state;int sequence;internal DateTime CommandReturnedUtc;internal int? CommandReturnCode;internal Dictionary<string,object> CommandReturnedDetails,LastDetails;
  internal static Dictionary<string,object> VerifiedJson(string path,string sha,int limit) {
   if(!EntitySets.Hex(sha,64))throw new ArgumentException("Invalid artifact SHA.");OwnedCopies.NoReparse(path);byte[] bytes;
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(file.Length>limit)throw new IOException("Artifact size limit.");using(var memory=new MemoryStream()){file.CopyTo(memory);bytes=memory.ToArray();}}
   using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=sha)throw new IOException("Artifact SHA changed.");return Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));
  }
  internal static string Freeze(string path,object value) {
   OwnedCopies.NoReparse(path);byte[] bytes=Encoding.UTF8.GetBytes(Program.Json.Serialize(value));
   using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
   using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
  }
  internal NativeJournal(string root,string operationId,string sha) {
   if(!EntitySets.Hex(operationId,32)||!EntitySets.Hex(sha,64))throw new ArgumentException("Invalid native journal identity.");
   id=operationId;planSha=sha;folder=Path.Combine(root,"operations",id);OwnedCopies.NoReparse(folder);
   if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Native operation journal already exists; replay forbidden.");Directory.CreateDirectory(folder);
  }
  internal static void Transition(string prior,string next,Dictionary<string,object> details) {
   bool allowed=prior==null?next=="prepared":prior=="prepared"?new[]{"execution_claimed","rejected_before_change"}.Contains(next):prior=="execution_claimed"?new[]{"command_started","rejected_before_change","outcome_unknown"}.Contains(next):prior=="command_started"?new[]{"command_returned","outcome_unknown"}.Contains(next):prior=="command_returned"?new[]{"executed_unverified","outcome_unknown","recovery_required","succeeded_verified"}.Contains(next):prior=="outcome_unknown"||prior=="executed_unverified"?new[]{"succeeded_verified","recovery_required"}.Contains(next):false;
   if(!allowed||details==null||Encoding.UTF8.GetByteCount(Program.Json.Serialize(details))>8192)throw new InvalidOperationException("Invalid native journal transition/details.");
   if(next=="succeeded_verified"||next=="recovery_required"||next=="rejected_before_change") {
    object evidence;if(!details.TryGetValue("evidence",out evidence))throw new InvalidOperationException("Independent evidence required.");
    var entries=evidence as System.Collections.IList;if(entries==null||entries.Count<1||entries.Count>16)throw new InvalidOperationException("Evidence list limit.");
    foreach(var entry in entries){var record=entry as Dictionary<string,object>;if(record==null||record.Count!=2||!record.ContainsKey("requestId")||!EntitySets.Hex(record["requestId"],32)||!record.ContainsKey("sha256")||!EntitySets.Hex(record["sha256"],64))throw new InvalidOperationException("Invalid evidence reference.");}
   }
  }
  internal void Append(string next,Dictionary<string,object> details) {
   Transition(state,next,details);string marker=Path.Combine(folder,"writer.lock");
   using(var file=new FileStream(marker,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.WriteByte(1);file.Flush(true);}
   // A failed/partial write intentionally leaves the marker and bytes for inspection.
   previous=Freeze(Path.Combine(folder,sequence.ToString("D6")+".json"),new Dictionary<string,object>{{"schemaVersion",1},{"operationId",id},{"planSha256",planSha},{"sequence",sequence},{"previousSha256",previous},{"state",next},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"details",details}});
   state=next;sequence++;File.Delete(marker);
  }
  internal string State {get{return state;}}
  NativeJournal(string root,string operationId,string sha,bool resume) {
   if(!EntitySets.Hex(operationId,32)||!EntitySets.Hex(sha,64))throw new ArgumentException("Invalid verification journal identity.");id=operationId;planSha=sha;folder=Path.Combine(root,"operations",id);OwnedCopies.NoReparse(folder);
   if(File.Exists(Path.Combine(folder,"writer.lock")))throw new IOException("Interrupted writer must be inspected; no automatic journal repair.");
   string[] files=Directory.GetFiles(folder,"*.json").OrderBy(x=>x,StringComparer.Ordinal).ToArray();if(files.Length<5||files.Length>6)throw new IOException("Owned-open journal sequence unsupported.");bool returned=false;
   foreach(string path in files) {
    OwnedCopies.NoReparse(path);if(Path.GetFileName(path)!=sequence.ToString("D6")+".json")throw new IOException("Native journal sequence invalid.");byte[] bytes;
    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(file.Length>65536)throw new IOException("Journal event size limit.");using(var memory=new MemoryStream()){file.CopyTo(memory);bytes=memory.ToArray();}}
    var value=Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
    if(value==null||value.Count!=8||!Equals(value["schemaVersion"],1)||!Equals(value["operationId"],id)||!Equals(value["planSha256"],sha)||!Equals(value["sequence"],sequence)||!Equals(value["previousSha256"],previous))throw new IOException("Native journal chain differs.");
    var eventUtc=EntitySets.Expiry(value["timestampUtc"]);var details=(Dictionary<string,object>)value["details"];Transition(state,(string)value["state"],details);state=(string)value["state"];LastDetails=details;returned|=state=="command_returned";if(state=="command_returned"){CommandReturnedUtc=eventUtc;CommandReturnedDetails=details;object code;if(details.TryGetValue("returnCode",out code)){if(!(code is int))throw new IOException("Journal return code must be an integer.");CommandReturnCode=(int)code;}}using(var hash=SHA256.Create())previous=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();sequence++;
   }
   if(!returned||!new[]{"outcome_unknown","executed_unverified","succeeded_verified"}.Contains(state))throw new IOException("Returned owned-open command required; no replay/claim allowed.");
  }
  internal static NativeJournal ForVerification(string root,string operationId,string sha){return new NativeJournal(root,operationId,sha,true);}
 }
}
