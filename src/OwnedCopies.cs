using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerShapeBridge {
 // Saved-file byte copies only. No COM, activation, SaveAs, model close or geometry command.
 public static class OwnedCopies {
  public const string Qualification="Saved disk bytes only; unsaved CAD changes are not included. Copies are not opened or certified loadable by PowerShape. Ownership/file guard is not a guard in the executor COM context. Execution remains disabled.";
  const long MaximumBytes=1073741824;
  [StructLayout(LayoutKind.Sequential)] struct NativeTime {public uint Low,High;}
  [StructLayout(LayoutKind.Sequential)] struct NativeInfo {public uint Attributes;public NativeTime Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle handle,out NativeInfo info);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateHardLink(string newName,string existingName,IntPtr security);

  public static string LocalPath(string path) {
   if(string.IsNullOrEmpty(path)||path.Length<4||!char.IsLetter(path[0])||path[1]!=':'||(path[2]!='\\'&&path[2]!='/')||path.Substring(2).Contains(':')||path.IndexOfAny(new[]{'\0','*','?','<','>','"','|'})>=0)throw new ArgumentException("An absolute local drive path without streams/device names is required.");
   string full=Path.GetFullPath(path.Replace('/','\\'));
   foreach(string part in path.Substring(3).Replace('/','\\').Split('\\'))if(part=="."||part==".."||part.EndsWith(".",StringComparison.Ordinal)||part.EndsWith(" ",StringComparison.Ordinal))throw new ArgumentException("Ambiguous path component.");
   return full;
  }
  public static void NoReparse(string path) {
   path=LocalPath(path);
   for(string current=path;!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current)) {
    if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse points/junctions are not accepted.");
   }
  }
  static string FinalPath(FileStream file) {
   var path=new StringBuilder(32768);uint size=GetFinalPathNameByHandle(file.SafeFileHandle,path,(uint)path.Capacity,0);
   if(size==0||size>=path.Capacity)throw new IOException("Final file path unavailable.");
   string value=path.ToString();if(value.StartsWith("\\\\?\\",StringComparison.Ordinal))value=value.Substring(4);
   return LocalPath(value);
  }
  static Dictionary<string,object> FileIdentity(FileStream file,string expected,bool singleLink) {
   NativeInfo info;if(!GetFileInformationByHandle(file.SafeFileHandle,out info))throw new IOException("File identity unavailable.");
   if(info.IndexHigh==0&&info.IndexLow==0||info.Creation.High==0&&info.Creation.Low==0||(info.Attributes&(uint)FileAttributes.ReparsePoint)!=0||singleLink&&info.Links!=1||!string.Equals(FinalPath(file),LocalPath(expected),StringComparison.OrdinalIgnoreCase))throw new IOException("File alias/reparse/hard-link identity rejected.");
   return new Dictionary<string,object>{{"volumeSerial",info.Volume.ToString("x8")},{"fileIndex",info.IndexHigh.ToString("x8")+info.IndexLow.ToString("x8")},{"creationTime",info.Creation.High.ToString("x8")+info.Creation.Low.ToString("x8")}};
  }
  static string Digest(FileStream file) {file.Position=0;using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
  static string IdentityKey(Dictionary<string,object> record) {return (string)record["volumeSerial"]+":"+(string)record["fileIndex"];}
  static void Distinct(params Dictionary<string,object>[] values) {if(values.Select(IdentityKey).Distinct().Count()!=values.Length)throw new IOException("Source, working file and checkpoint must be independent files.");}
  static string Freeze(string path,object value) {
   byte[] bytes=Encoding.UTF8.GetBytes(Program.Json.Serialize(value));
   using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
   return Program.Hash(path);
  }
  static string Folder(string root,string id) {
   if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid working-copy identity.");
   string folder=Path.Combine(LocalPath(root.TrimEnd('\\','/')),"workingcopies",id);NoReparse(folder);return folder;
  }
  public static void ValidatePrepare(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("sourcePath")||!(args["sourcePath"] is string)||!args.ContainsKey("sourceSha256")||!EntitySets.Hex(args["sourceSha256"],64))throw new ArgumentException("Invalid saved working-copy parameters.");
   if(!string.Equals(Path.GetExtension(LocalPath((string)args["sourcePath"])),".psmodel",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A saved psmodel source is required; STEP conversion requires a separate CAD import.");
  }
  public static void ValidateHandle(Dictionary<string,object> args) {
   if(args.Count!=2||!args.ContainsKey("copyId")||!EntitySets.Hex(args["copyId"],32)||!args.ContainsKey("ownershipSha256")||!EntitySets.Hex(args["ownershipSha256"],64))throw new ArgumentException("Invalid working-copy handle parameters.");
  }
  public static Dictionary<string,object> Create(string root,string id,string sourcePath,string expectedSha) {
   ValidatePrepare(new Dictionary<string,object>{{"sourcePath",sourcePath},{"sourceSha256",expectedSha}});
   sourcePath=LocalPath(sourcePath);NoReparse(sourcePath);string folder=Folder(root,id);
   if(Directory.Exists(folder)||File.Exists(folder))throw new IOException("Working-copy identity already exists; never overwrite or retry it.");
   // Deny writes/deletes for the whole read. A busy CAD source is rejected, never copied with weaker sharing.
   using(var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    long length=source.Length;if(length<1||length>MaximumBytes)throw new IOException("Saved source size outside supported limit.");
    var sourceIdentity=FileIdentity(source,sourcePath,false);
    if(Digest(source)!=expectedSha)throw new IOException("Saved source hash changed before copying.");
    source.Position=0;
    Directory.CreateDirectory(Path.GetDirectoryName(folder));NoReparse(Path.GetDirectoryName(folder));
    string reservation=folder+".reservation.json";
    Freeze(reservation,new{copyId=id,state="reserved_disk_copy",sourcePath=sourcePath,sourceSha256=expectedSha,cadMutationAttempted=false});
    Directory.CreateDirectory(folder);NoReparse(folder);
    if(Directory.GetFileSystemEntries(folder).Length!=0)throw new IOException("Working-copy directory is not empty.");
    string checkpointFolder=Path.Combine(folder,"checkpoint");Directory.CreateDirectory(checkpointFolder);NoReparse(checkpointFolder);
    string working=Path.Combine(folder,"Working.psmodel"),checkpoint=Path.Combine(checkpointFolder,"Before.psmodel");
    using(var work=new FileStream(working,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))using(var before=new FileStream(checkpoint,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
     var workIdentity=FileIdentity(work,working,true);var checkpointIdentity=FileIdentity(before,checkpoint,true);Distinct(sourceIdentity,workIdentity,checkpointIdentity);
     byte[] buffer=new byte[65536];int size;long written=0;
     while((size=source.Read(buffer,0,buffer.Length))>0){work.Write(buffer,0,size);before.Write(buffer,0,size);written+=size;if(written>MaximumBytes)throw new IOException("Source exceeded copy limit.");}
     work.Flush(true);before.Flush(true);
     if(written!=length||source.Length!=length||Digest(source)!=expectedSha||Digest(work)!=expectedSha||Digest(before)!=expectedSha)throw new IOException("Copy bytes differ from locked source.");
     var manifest=new Dictionary<string,object>{{"schemaVersion",1},{"copyId",id},{"bridgeVersion",Program.ReleaseVersion},{"createdUtc",DateTime.UtcNow.ToString("o")},{"scope","saved_disk_bytes_only"},{"executionEnabled",false},
      {"source",new Dictionary<string,object>{{"path",sourcePath},{"sha256",expectedSha},{"length",length},{"fileIdentity",sourceIdentity}}},
      {"working",new Dictionary<string,object>{{"path",working},{"initialSha256",expectedSha},{"length",length},{"fileIdentity",workIdentity}}},
      {"checkpoint",new Dictionary<string,object>{{"path",checkpoint},{"sha256",expectedSha},{"length",length},{"fileIdentity",checkpointIdentity}}}};
     // ReadOnly adds a filesystem guard; a future executor must still reject checkpoint paths.
     File.SetAttributes(checkpoint,FileAttributes.ReadOnly);
     string ownershipSha=Freeze(Path.Combine(folder,"ownership.json"),manifest);
     return new Dictionary<string,object>{{"state","working_copy_prepared_disk"},{"copyId",id},{"ownershipSha256",ownershipSha},{"ownership",manifest},{"workingFileChanged",false},{"cadMutationAttempted",false},{"powerShapeConnectionAttempted",false},{"qualification",Qualification}};
    }
   }
  }
  static Dictionary<string,object> ReadOwnership(string root,string id,string sha,out FileStream heldReceipt) {
   heldReceipt=null;
   ValidateHandle(new Dictionary<string,object>{{"copyId",id},{"ownershipSha256",sha}});string path=Path.Combine(Folder(root,id),"ownership.json");NoReparse(path);
   byte[] bytes;var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);heldReceipt=file;
   {
    if(file.Length>65536)throw new IOException("Ownership receipt exceeds limit.");FileIdentity(file,path,true);
    using(var memory=new MemoryStream()){file.CopyTo(memory);bytes=memory.ToArray();}
   }
   using(var digest=SHA256.Create())if(BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=sha)throw new IOException("Ownership receipt changed.");
   var manifest=Program.Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(bytes));
   if(manifest==null||manifest.Count!=9||!Equals(manifest["schemaVersion"],1)||!Equals(manifest["copyId"],id)||!Equals(manifest["scope"],"saved_disk_bytes_only")||!Equals(manifest["executionEnabled"],false))throw new IOException("Invalid ownership receipt.");
   return manifest;
  }
  static Dictionary<string,object> Record(Dictionary<string,object> manifest,string field) {
   var result=manifest[field] as Dictionary<string,object>;if(result==null||result.Count!=4||!(result["path"] is string)||!(result["fileIdentity"] is Dictionary<string,object>))throw new IOException("Invalid file ownership record.");
   LocalPath((string)result["path"]);var identity=(Dictionary<string,object>)result["fileIdentity"];
   if(identity.Count!=3||!EntitySets.Hex(identity["volumeSerial"],8)||!EntitySets.Hex(identity["fileIndex"],16)||!EntitySets.Hex(identity["creationTime"],16))throw new IOException("Invalid owned file identity.");
   string hashKey=field=="working"?"initialSha256":"sha256";
   if(!EntitySets.Hex(result[hashKey],64)||!(result["length"] is int)&&!(result["length"] is long)||Convert.ToInt64(result["length"])<1||Convert.ToInt64(result["length"])>MaximumBytes)throw new IOException("Invalid owned file size/hash.");
   return result;
  }
  internal sealed class OwnershipLease : IDisposable {
   internal FileStream Receipt,Work,Before;
   internal Dictionary<string,object> Inspection;
   internal string WorkingPath,CheckpointPath,CheckpointSha;
   internal long CheckpointLength;
   internal void CheckPath(string path) {
    if(!string.Equals(LocalPath(path),WorkingPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("active_document_not_owned_working_copy");
   }
   internal void Recheck() {
    NoReparse(WorkingPath);NoReparse(CheckpointPath);
    var manifest=(Dictionary<string,object>)Inspection["ownership"];
    if(EntitySets.Canonical(FileIdentity(Work,WorkingPath,true))!=EntitySets.Canonical(Record(manifest,"working")["fileIdentity"])||EntitySets.Canonical(FileIdentity(Before,CheckpointPath,true))!=EntitySets.Canonical(Record(manifest,"checkpoint")["fileIdentity"]))throw new IOException("Owned file identity changed during guard.");
    if((File.GetAttributes(CheckpointPath)&FileAttributes.ReadOnly)==0||Before.Length!=CheckpointLength||Digest(Before)!=CheckpointSha)throw new IOException("Independent checkpoint changed during guard.");
    if(Digest(Work)!=(string)Inspection["workingSha256"])throw new IOException("Working disk bytes changed during guard.");
   }
   public void Dispose(){if(Before!=null)Before.Dispose();if(Work!=null)Work.Dispose();if(Receipt!=null)Receipt.Dispose();}
  }
  internal static OwnershipLease Hold(string root,string id,string sha) {
   var lease=new OwnershipLease();
   try {
   string folder=Folder(root,id);var manifest=ReadOwnership(root,id,sha,out lease.Receipt);var source=Record(manifest,"source");var working=Record(manifest,"working");var checkpoint=Record(manifest,"checkpoint");
   string workingPath=Path.Combine(folder,"Working.psmodel"),checkpointPath=Path.Combine(folder,"checkpoint","Before.psmodel");
   if(!Equals(source["sha256"],checkpoint["sha256"])||!Equals(source["sha256"],working["initialSha256"])||Convert.ToInt64(source["length"])!=Convert.ToInt64(checkpoint["length"])||Convert.ToInt64(source["length"])!=Convert.ToInt64(working["length"]))throw new IOException("Initial source/checkpoint/copy bytes differ.");
   if(!string.Equals((string)working["path"],workingPath,StringComparison.OrdinalIgnoreCase)||!string.Equals((string)checkpoint["path"],checkpointPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Owned file path differs from fixed layout.");
   NoReparse(workingPath);NoReparse(checkpointPath);
   // Inspection tolerates legitimate working-byte changes, but not replacement/aliasing.
   lease.Work=new FileStream(workingPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);lease.Before=new FileStream(checkpointPath,FileMode.Open,FileAccess.Read,FileShare.Read);
   var work=lease.Work;var before=lease.Before;
   {
    var workIdentity=FileIdentity(work,workingPath,true);var beforeIdentity=FileIdentity(before,checkpointPath,true);var sourceIdentity=(Dictionary<string,object>)source["fileIdentity"];
    Distinct(sourceIdentity,workIdentity,beforeIdentity);
    if(EntitySets.Canonical(workIdentity)!=EntitySets.Canonical(working["fileIdentity"])||EntitySets.Canonical(beforeIdentity)!=EntitySets.Canonical(checkpoint["fileIdentity"]))throw new IOException("Owned file was replaced.");
    if((File.GetAttributes(checkpointPath)&FileAttributes.ReadOnly)==0||before.Length!=Convert.ToInt64(checkpoint["length"])||Digest(before)!=(string)checkpoint["sha256"])throw new IOException("Independent checkpoint changed or lost protection.");
    if(work.Length<1||work.Length>MaximumBytes)throw new IOException("Working file size outside supported limit.");
    string workSha=Digest(work);
    lease.Inspection=new Dictionary<string,object>{{"state","working_copy_inspected_disk"},{"copyId",id},{"ownershipSha256",sha},{"ownership",manifest},{"workingSha256",workSha},{"workingFileChanged",workSha!=(string)working["initialSha256"]},{"checkpointVerified",true},{"cadMutationAttempted",false},{"powerShapeConnectionAttempted",false},{"qualification",Qualification}};
    lease.WorkingPath=workingPath;lease.CheckpointPath=checkpointPath;lease.CheckpointSha=(string)checkpoint["sha256"];lease.CheckpointLength=Convert.ToInt64(checkpoint["length"]);
    return lease;
   }
   }catch{lease.Dispose();throw;}
  }
  public static Dictionary<string,object> Inspect(string root,string id,string sha) {
   using(var lease=Hold(root,id,sha))return lease.Inspection;
  }
  public static void CheckTarget(string root,string id,string sha,string activeDocumentPath) {
   var result=Inspect(root,id,sha);var manifest=(Dictionary<string,object>)result["ownership"];string working=(string)Record(manifest,"working")["path"];
   if(!string.Equals(LocalPath(activeDocumentPath),working,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Active document is not this owned working file; source/checkpoint/unowned targets are forbidden.");
   // This method checks files/path only. A qualified executor must call it inside its COM guard.
  }
  public static Dictionary<string,object> Run(string requestId,Dictionary<string,object> args,bool prepare) {
   var result=new Dictionary<string,object>{{"state","working_copy_rejected"},{"bridgeVersion",Program.ReleaseVersion},{"timestampUtc",DateTime.UtcNow.ToString("o")},{"cadMutationAttempted",false},{"powerShapeConnectionAttempted",false},{"qualification",Qualification}};
   try {
    Config config=Program.ReadConfig();if(!config.IsGeneralCore||!config.workingCopiesExperimental)throw new InvalidOperationException("Experimental working copies disabled.");
    if(prepare){ValidatePrepare(args);result=Create(Program.Root,requestId,(string)args["sourcePath"],(string)args["sourceSha256"]);}
    else {ValidateHandle(args);result=Inspect(Program.Root,(string)args["copyId"],(string)args["ownershipSha256"]);}
    result["bridgeVersion"]=Program.ReleaseVersion;result["timestampUtc"]=DateTime.UtcNow.ToString("o");
   }catch(Exception error){result["error"]=error.Message;result["errorCode"]=error is IOException&&new[]{32,33}.Contains(error.HResult&0xffff)?"file_busy":"working_copy_file_or_policy_rejected";if(prepare&&EntitySets.Hex(requestId,32)){result["copyId"]=requestId;result["localArtifactsMayExist"]=true;}}
   return result;
  }
  public static int SelfTest() {
   int count=0;Action<bool> check=ok=>{if(!ok)throw new Exception("Owned-copy self-test failed at "+count);count++;};
   Action<Action> rejects=action=>{bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(IOException){rejected=true;}catch(InvalidOperationException){rejected=true;}check(rejected);};
   string root=Path.Combine(Program.Root,"work","owned-copy-self-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   string source=Path.Combine(root,"Source.psmodel");File.WriteAllText(source,"Opaque saved disk fixture, not a CAD model.");string sourceSha=Program.Hash(source);string id=Guid.NewGuid().ToString("N");
   var made=Create(root,id,source,sourceSha);check(Equals(made["state"],"working_copy_prepared_disk"));string sha=(string)made["ownershipSha256"];
   var manifest=(Dictionary<string,object>)made["ownership"];string work=(string)Record(manifest,"working")["path"],before=(string)Record(manifest,"checkpoint")["path"];
   check(Program.Hash(work)==sourceSha&&Program.Hash(before)==sourceSha&&Program.Hash(source)==sourceSha);
   CheckTarget(root,id,sha,work);count++;
   foreach(string target in new[]{source,before,Path.Combine(root,"Unowned.psmodel")})rejects(()=>CheckTarget(root,id,sha,target));
   rejects(()=>Create(root,id,source,sourceSha));rejects(()=>Create(root,Guid.NewGuid().ToString("N"),source,new string('a',64)));
   foreach(string path in new[]{"relative.psmodel","\\\\server\\part.psmodel","D:\\Part.psmodel:stream","D:\\Folder\\..\\Part.psmodel","D:\\Part.psmodel.","\\\\?\\D:\\Part.psmodel"})rejects(()=>LocalPath(path));
   rejects(()=>ValidatePrepare(new Dictionary<string,object>{{"sourcePath",source},{"sourceSha256",sourceSha},{"command","SAVE"}}));
   rejects(()=>ValidatePrepare(new Dictionary<string,object>{{"sourcePath",Path.ChangeExtension(source,"step")},{"sourceSha256",sourceSha}}));
   using(var busy=new FileStream(source,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))rejects(()=>Create(root,Guid.NewGuid().ToString("N"),source,sourceSha));
   File.AppendAllText(work," Own working bytes changed.");var changed=Inspect(root,id,sha);check(Equals(changed["workingFileChanged"],true));check(Program.Hash(source)==sourceSha&&Program.Hash(before)==sourceSha);
   using(var activeWorking=new FileStream(work,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite)){CheckTarget(root,id,sha,work);count++;}
   string secondId=Guid.NewGuid().ToString("N");var second=Create(root,secondId,source,sourceSha);string secondSha=(string)second["ownershipSha256"];var secondManifest=(Dictionary<string,object>)second["ownership"];string replaced=(string)Record(secondManifest,"working")["path"];
   File.Delete(replaced);File.WriteAllText(replaced,File.ReadAllText(source));rejects(()=>Inspect(root,secondId,secondSha));
   string thirdId=Guid.NewGuid().ToString("N");var third=Create(root,thirdId,source,sourceSha);string thirdSha=(string)third["ownershipSha256"];var thirdManifest=(Dictionary<string,object>)third["ownership"];string linked=(string)Record(thirdManifest,"working")["path"];
   File.Delete(linked);if(!CreateHardLink(linked,source,IntPtr.Zero))throw new IOException("Hard-link refusal test fixture could not be created.");rejects(()=>Inspect(root,thirdId,thirdSha));File.Delete(linked);
   File.SetAttributes(before,FileAttributes.Normal);rejects(()=>Inspect(root,id,sha));File.AppendAllText(before,"changed");File.SetAttributes(before,FileAttributes.ReadOnly);rejects(()=>Inspect(root,id,sha));
   string manifestPath=Path.Combine(Folder(root,id),"ownership.json");File.AppendAllText(manifestPath," ");rejects(()=>Inspect(root,id,sha));
   if(Program.ReadConfig().workingCopiesExperimental) {
    string requestId=new string('c',32);
    foreach(string recipe in new[]{"prepare_working_copy","inspect_working_copy"}) {
     object parameters=recipe=="prepare_working_copy"?(object)new{sourcePath=source,sourceSha256=sourceSha}:new{copyId=id,ownershipSha256=sha};
     FileRequests.Parse(requestId,Program.Json.Serialize(new{requestId=requestId,operation="recipe.prepare",arguments=new{recipeId=recipe,parameters=parameters}}));count++;
    }
    rejects(()=>FileRequests.Parse(requestId,Program.Json.Serialize(new{requestId=requestId,operation="recipe.prepare",arguments=new{recipeId="prepare_working_copy",parameters=new{sourcePath=source,sourceSha256=sourceSha,overwrite=true}}})));
   }
   return count;
 }
}
}
