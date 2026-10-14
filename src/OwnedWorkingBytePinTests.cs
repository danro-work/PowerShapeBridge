using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;

namespace PowerShapeBridge {
 internal static class OwnedWorkingBytePinTests {
  static void Require(bool value,string error){if(!value)throw new InvalidOperationException(error);}
  static void Refuse(Action action){try{action();}catch(IOException){return;}throw new InvalidOperationException("Ordinary IO was accepted while pinned.");}
  internal static object[] Run() {
   string folder=Path.Combine(Program.Root,"work","working-byte-pin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   string path=Path.Combine(folder,"synthetic.bin");byte[] original=Enumerable.Range(0,100).Select(i=>(byte)i).ToArray();File.WriteAllBytes(path,original);
   string initial=Program.Hash(path);var rows=new List<object>();
   Action<string> pass=name=>{Require(Program.Hash(path)==initial,"Synthetic bytes changed: "+name);rows.Add(new Dictionary<string,object>{{"case",name},{"state","passed"},{"path",path},{"sha256",initial},{"cadConnectionAttempted",false}});};
   using(var writer=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite,1)) {
    using(var pin=new OwnedWorkingBytePin(path)) {
     Require(OwnedCopies.Digest(pin.Stream)==initial&&Program.Hash(path)==initial,"Other readers cannot read shared pin.");pass("existing_writer_read");
     Refuse(()=>{writer.Position=0;writer.Write(new byte[]{255},0,1);writer.Flush(true);});pass("overwrite_blocked");
     Refuse(()=>{writer.Position=writer.Length;writer.Write(new byte[]{255},0,1);writer.Flush(true);});pass("append_blocked");
     writer.SetLength(99);Require(OwnedCopies.Digest(pin.Stream)!=initial,"Truncate not detected by SHA.");writer.SetLength(100);
     using(var restoreMap=MemoryMappedFile.CreateFromFile(writer,null,0,MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,true))using(var restoreView=restoreMap.CreateViewAccessor()){restoreView.Write(99,original[99]);restoreView.Flush();}
     pass("truncate_detected_by_sha");
     Refuse(()=>File.Move(path,path+"-renamed"));pass("rename_blocked");
     using(var second=new OwnedWorkingBytePin(path))Require(OwnedCopies.Digest(second.Stream)==initial,"Second shared reader failed.");pass("second_shared_reader");
     // Windows mapped views bypass range locks. Demonstrate the limitation,
     // detect the change by retained SHA and restore only this synthetic file.
     using(var map=MemoryMappedFile.CreateFromFile(writer,null,0,MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,true))using(var view=map.CreateViewAccessor()) {
      view.Write(0,(byte)255);view.Flush();Require(OwnedCopies.Digest(pin.Stream)!=initial,"Mapped-view change not detected by SHA.");
      view.Write(0,original[0]);view.Flush();Require(OwnedCopies.Digest(pin.Stream)==initial,"Synthetic mapped restore failed.");
     }
     pass("mapped_write_detected_by_sha");
    }
    writer.Position=0;writer.Write(new byte[]{255},0,1);writer.Flush(true);Require(Program.Hash(path)!=initial,"Pin not released on disposal.");
    writer.Position=0;writer.Write(new[]{original[0]},0,1);writer.Flush(true);pass("released_write_allowed");
    writer.Lock(0,100);
    try {Refuse(()=>{using(var rejected=new OwnedWorkingBytePin(path)){} });}
    finally {writer.Unlock(0,100);}
    pass("exclusive_lock_refused");
   }
   return rows.ToArray();
  }
 }
}
