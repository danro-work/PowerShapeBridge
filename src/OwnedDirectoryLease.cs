using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerShapeBridge {
 // A directory handle denies rename/delete for the duration of a file operation.
 // It never connects to CAD and does not authorize a command by itself.
 internal sealed class OwnedDirectoryLease : IDisposable {
  [StructLayout(LayoutKind.Sequential)] struct NativeTime {public uint Low,High;}
  [StructLayout(LayoutKind.Sequential)] struct NativeInfo {
   public uint Attributes;public NativeTime Creation,Access,Write;
   public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool GetFileInformationByHandle(SafeFileHandle handle,out NativeInfo info);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
  readonly SafeFileHandle handle;
  readonly string path;
  internal readonly Dictionary<string,object> Identity;
  internal OwnedDirectoryLease(string directory) {
   path=OwnedCopies.LocalPath(directory);OwnedCopies.NoReparse(path);
   // Attribute-only access does not participate in Windows share-delete checks.
   // Generic read includes directory listing access, so rename/delete is denied.
   handle=CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
   try {
    if(handle.IsInvalid)throw new IOException("Owned directory handle unavailable.");
    Identity=ReadIdentity();
   }catch {handle.Dispose();throw;}
  }
  Dictionary<string,object> ReadIdentity() {
   OwnedCopies.NoReparse(path);NativeInfo info;
   if(!GetFileInformationByHandle(handle,out info)||(info.Attributes&0x10)==0||(info.Attributes&0x400)!=0||
      info.IndexHigh==0&&info.IndexLow==0)throw new IOException("Owned directory identity unavailable/reparse.");
   var final=new StringBuilder(32768);uint size=GetFinalPathNameByHandle(handle,final,(uint)final.Capacity,0);
   if(size==0||size>=final.Capacity)throw new IOException("Owned directory final path unavailable.");
   string value=final.ToString();if(value.StartsWith("\\\\?\\",StringComparison.Ordinal))value=value.Substring(4);
   if(!string.Equals(OwnedCopies.LocalPath(value),path,StringComparison.OrdinalIgnoreCase))throw new IOException("Owned directory was moved or aliased.");
   return new Dictionary<string,object>{{"volumeSerial",info.Volume.ToString("x8")},
    {"fileIndex",info.IndexHigh.ToString("x8")+info.IndexLow.ToString("x8")},
    {"creationTime",info.Creation.High.ToString("x8")+info.Creation.Low.ToString("x8")}};
  }
  internal void Recheck() {
   if(EntitySets.Canonical(ReadIdentity())!=EntitySets.Canonical(Identity))throw new IOException("Owned directory identity changed.");
  }
  public void Dispose(){handle.Dispose();}
 }
}
