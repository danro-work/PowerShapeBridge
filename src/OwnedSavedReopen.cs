using System;
using System.Collections.Generic;
using System.IO;

namespace PowerShapeBridge {
 // Creates a closed ReadOnly seed, then calls the unchanged strict source-copy
 // routine. No open/selection/activation/save/geometry command.
 internal static class OwnedSavedReopen {
  internal const string Qualification="Saved-byte reopen preparation only. Independently verified result is read with a held physical identity and double SHA checks around a create-once seed snapshot; not an exclusive writer lock or topology proof. Separate guarded OPEN and fresh verification/new binding required. No CAD command or automatic retry.";
  internal static Dictionary<string,object> Run(string id,Dictionary<string,object> args) {
   var result=new Dictionary<string,object>{{"state","owned_saved_reopen_preparation_rejected"},{"executionEnabled",false},{"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"powerShapeConnectionAttempted",false},{"retryAllowed",false},{"qualification",Qualification}};
   try{Config config=Program.ReadConfig();if(!config.IsGeneralCore||!config.ownedSavedReopenExperimental)throw new InvalidOperationException("Experimental saved reopen disabled.");result=Prepare(Program.Root,id,args);}
   catch(Exception error){result["error"]=error.Message;result["localArtifactsMayExist"]=true;result["reopenId"]=id;}
   result["timestampUtc"]=DateTime.UtcNow.ToString("o");result["bridgeVersion"]=Program.ReleaseVersion;return result;
  }
  internal static Dictionary<string,object> Prepare(string root,string id,Dictionary<string,object> args,Action<string> duringCopy=null) {
   OwnedSavedSource.Validate(args);if(!EntitySets.Hex(id,32))throw new ArgumentException("Invalid saved reopen identity.");root=OwnedCopies.LocalPath(root.TrimEnd('\\','/'));
   string parent=Path.Combine(root,"savedreopens"),folder=Path.Combine(parent,id),seed=Path.Combine(folder,"DiskReopen_"+id+".psmodel");
   OwnedCopies.NoReparse(folder);if(Directory.Exists(folder)||File.Exists(folder)||Directory.Exists(Path.Combine(root,"workingcopies",id)))throw new IOException("Saved reopen identity exists; no replay/overwrite.");
   using(var source=new OwnedSavedSource(root,args)) {
    Directory.CreateDirectory(parent);OwnedCopies.NoReparse(parent);
    using(var rootPin=new OwnedDirectoryLease(root))using(var parentPin=new OwnedDirectoryLease(parent)) {
     NativeJournal.Freeze(folder+".reservation.json",new Dictionary<string,object>{{"schemaVersion",1},{"reopenId",id},{"verificationRequestId",args["verificationRequestId"]},{"verificationReceiptSha256",args["verificationReceiptSha256"]},{"automaticReplayForbidden",true}});
     Directory.CreateDirectory(folder);
     using(var pin=new OwnedDirectoryLease(folder)) {
      source.Recheck();source.File.Position=0;
      using(var output=new FileStream(seed,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
       byte[] buffer=new byte[65536];int size;long written=0;
       while((size=source.File.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,size);written+=size;if(written>1073741824)throw new IOException("Saved seed copy exceeded size limit.");}
       output.Flush(true);if(duringCopy!=null)duringCopy(seed);
       if(written!=Convert.ToInt64(source.Saved["length"])||output.Length!=written||OwnedCopies.Digest(output)!=(string)source.Saved["sha256"])throw new IOException("Saved seed bytes differ.");
       source.Recheck();rootPin.Recheck();parentPin.Recheck();pin.Recheck();
      }
      File.SetAttributes(seed,FileAttributes.ReadOnly);var seedProof=OwnedSaveAs.SavedProof(seed);
      var copy=OwnedCopies.Create(root,id,seed,(string)source.Saved["sha256"]);
      using(var owned=OwnedCopies.Hold(root,id,(string)copy["ownershipSha256"])) {
       source.Recheck();owned.Recheck();rootPin.Recheck();parentPin.Recheck();pin.Recheck();
       var plan=new Dictionary<string,object>{{"schemaVersion",1},{"reopenId",id},{"operation","reopen_owned_saved_result"},{"bridgeVersion",Program.ReleaseVersion},{"preparedUtc",DateTime.UtcNow.ToString("o")},{"expiresUtc",source.Expires.ToString("o")},
        {"executionEnabled",false},{"verificationRequestId",args["verificationRequestId"]},{"verificationReceiptSha256",args["verificationReceiptSha256"]},{"saveOperationId",source.Verified["saveOperationId"]},
        {"targetId",source.Plan["targetId"]},{"targetSha256",source.Plan["targetSha256"]},{"savedResult",source.Saved},{"seed",seedProof},
        {"copyId",id},{"ownershipSha256",copy["ownershipSha256"]},{"workingPath",owned.WorkingPath},{"workingSha256",owned.Inspection["workingSha256"]},{"checkpointPath",owned.CheckpointPath},{"checkpointSha256",owned.CheckpointSha},{"directoryIdentity",pin.Identity}};
       string sha=NativeJournal.Freeze(Path.Combine(folder,"reopen.json"),plan);source.Recheck();owned.Recheck();pin.Recheck();
       return new Dictionary<string,object>{{"state","owned_saved_reopen_prepared_files"},{"reopenId",id},{"reopenSha256",sha},{"reopen",plan},{"executionEnabled",false},
        {"cadSaveAttempted",false},{"contextChangeAttempted",false},{"geometryMutationAttempted",false},{"powerShapeConnectionAttempted",false},{"qualification",Qualification}};
      }
     }
    }
   }
  }
 }
}
