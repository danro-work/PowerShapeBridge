using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;

namespace PowerShapeMcp;

// Point-in-time identity read only. Native executor keeps the longer directory lease.
internal static class WindowsDirectoryIdentity
{
    [StructLayout(LayoutKind.Sequential)] private struct Time {public uint Low,High;}
    [StructLayout(LayoutKind.Sequential)] private struct Info
    {
        public uint Attributes;public Time Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out Info info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint count,uint flags);
    internal static JsonObject Read(string path)
    {
        path=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        using var handle=CreateFile(path,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        if(handle.IsInvalid||!GetFileInformationByHandle(handle,out var value)||(value.Attributes&0x10)==0||(value.Attributes&0x400)!=0||value.IndexHigh==0&&value.IndexLow==0)
            throw new IOException("Directory identity unavailable or aliased.");
        var final=new StringBuilder(32768);var size=GetFinalPathNameByHandle(handle,final,(uint)final.Capacity,0);
        if(size==0||size>=final.Capacity)throw new IOException("Directory final path unavailable.");
        var actual=final.ToString();if(actual.StartsWith("\\\\?\\",StringComparison.Ordinal))actual=actual[4..];
        if(ObservationPolicy.NormalizePath(actual)!=ObservationPolicy.NormalizePath(path))throw new IOException("Directory final path differs.");
        return new(){["volumeSerial"]=value.Volume.ToString("x8"),["fileIndex"]=value.IndexHigh.ToString("x8")+value.IndexLow.ToString("x8"),["creationTime"]=value.Creation.High.ToString("x8")+value.Creation.Low.ToString("x8")};
    }
}
