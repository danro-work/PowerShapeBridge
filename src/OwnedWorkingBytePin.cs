using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PowerShapeBridge {
 // Windows shared byte-range lock: reads and existing writer handles may remain
 // open, but ordinary data writes are denied until disposal. EOF changes and
 // mapped views bypass range locks; retained identity/length/SHA checks apply.
 internal sealed class OwnedWorkingBytePin : IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct Offset {internal IntPtr Internal,InternalHigh;internal uint Low,High;internal IntPtr Event;}
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool LockFileEx(SafeFileHandle file,uint flags,uint reserved,uint low,uint high,ref Offset offset);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool UnlockFileEx(SafeFileHandle file,uint reserved,uint low,uint high,ref Offset offset);
  internal FileStream Stream {get;private set;}
  // Every supported data byte, including allowed growth, is below this bound.
  // Do not lock CFB's reserved 0x7FFFFF00..0x7FFFFFFF transaction/lock sector.
  const uint DataLockBytes=(uint)OwnedCopies.MaximumBytes;
  Offset offset;bool locked;
  internal OwnedWorkingBytePin(string path) {
   try {
    OwnedCopies.NoReparse(path);
    Stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
    if(Stream.Length<1||Stream.Length>OwnedCopies.MaximumBytes)throw new IOException("Owned working file outside supported data size.");
    // Fail immediately; no GUI wait or blocking retry. No EXCLUSIVE_LOCK flag:
    // all readers can continue, including other retained ownership handles.
    if(!LockFileEx(Stream.SafeFileHandle,1,0,DataLockBytes,0,ref offset)) {
     int code=Marshal.GetLastWin32Error();throw new IOException("Owned working byte lock unavailable (Win32 "+code+").",new Win32Exception(code));
    }
    locked=true;
   }catch{Dispose();throw;}
  }
  public void Dispose() {
   if(Stream==null)return;
   int error=0;
   try {if(locked&&!UnlockFileEx(Stream.SafeFileHandle,0,DataLockBytes,0,ref offset))error=Marshal.GetLastWin32Error();}
   finally {locked=false;Stream.Dispose();Stream=null;}
   if(error!=0)throw new IOException("Owned working byte unlock failed; handle closed.",new Win32Exception(error));
  }
 }
}
