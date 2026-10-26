using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
namespace PowerShapeBridge {
 // Explicit attachment identity, not process discovery by ordering or a COM launcher.
 internal static class PowerShapeProcessPin {
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr hwnd);
  internal sealed class Identity {internal int ProcessId,Session;internal DateTime StartedUtc;internal string Executable;}
  static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
  internal static Identity Definition(Dictionary<string,object> pin){
   Need(pin!=null&&pin.Count==4&&new[]{"processId","processStartedUtc","windowsSessionId","executablePath"}.All(pin.ContainsKey),"Exact PowerShape process pin fields required.");
   Need(pin["processId"]is int&&(int)pin["processId"]>0&&pin["windowsSessionId"]is int&&(int)pin["windowsSessionId"]>=0&&pin["processStartedUtc"]is string&&pin["executablePath"]is string,"Strict PowerShape process pin types required.");
   string stamp=(string)pin["processStartedUtc"],path=(string)pin["executablePath"];DateTimeOffset started;
   Need(Regex.IsMatch(stamp,@"\A\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)\z")&&DateTimeOffset.TryParse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None,out started),"Explicit UTC process creation timestamp required.");
   // Parse after the guarded TryParse to keep definite assignment on Framework csc.
   started=DateTimeOffset.Parse(stamp,CultureInfo.InvariantCulture,DateTimeStyles.None);
   Need(path.Length<=1024&&Regex.IsMatch(path,@"\A[A-Za-z]:\\")&&string.Equals(Path.GetFullPath(path),path,StringComparison.OrdinalIgnoreCase)&&string.Equals(Path.GetFileName(path),"powershape.exe",StringComparison.OrdinalIgnoreCase),"Canonical absolute PowerShape executable path required.");
   return new Identity{ProcessId=(int)pin["processId"],Session=(int)pin["windowsSessionId"],StartedUtc=started.UtcDateTime,Executable=path};
  }
  internal static void Match(Identity expected,int comPid,uint windowPid,int currentSession,int processId,string processName,DateTime startedUtc,int session,string executable){
   Need(expected!=null&&comPid==expected.ProcessId&&windowPid==(uint)expected.ProcessId&&processId==expected.ProcessId&&session==expected.Session&&session==currentSession&&startedUtc.ToUniversalTime().Ticks==expected.StartedUtc.Ticks&&string.Equals(processName,"powershape",StringComparison.OrdinalIgnoreCase)&&string.Equals(Path.GetFullPath(executable),expected.Executable,StringComparison.OrdinalIgnoreCase),"Pinned PowerShape COM/window/process identity differs; no fallback.");
  }
  internal static void ValidateConfiguration(Dictionary<string,object> pin,bool general){if(pin==null)return;Need(general,"Process pin requires general-v1.");Definition(pin);}
  internal static void Check(object application,Dictionary<string,object> pin){
   if(pin==null)return;var expected=Definition(pin);dynamic ps=application;int pid=Program.StrictInt(ps.Evaluate("app.pid"));long raw=Convert.ToInt64(ps.MainWindowHWND,CultureInfo.InvariantCulture);var hwnd=new IntPtr(raw<0&&raw>=int.MinValue?(long)unchecked((uint)raw):raw);uint owner;Need(IsWindow(hwnd)&&GetWindowThreadProcessId(hwnd,out owner)!=0,"Pinned COM main window unavailable.");
   // Read owner again independently; no window selection, activation or message is sent.
   GetWindowThreadProcessId(hwnd,out owner);using(var process=Process.GetProcessById(pid))using(var current=Process.GetCurrentProcess())Match(expected,pid,owner,current.SessionId,process.Id,process.ProcessName,process.StartTime.ToUniversalTime(),process.SessionId,process.MainModule.FileName);
   Need(Program.StrictInt(ps.Evaluate("app.pid"))==pid&&Convert.ToInt64(ps.MainWindowHWND,CultureInfo.InvariantCulture)==raw,"Pinned COM application/window changed during identity read.");
  }
 }
}
