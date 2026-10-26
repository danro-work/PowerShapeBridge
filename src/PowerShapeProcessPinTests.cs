using System;
using System.Collections.Generic;
using System.IO;
namespace PowerShapeBridge {
 internal static class PowerShapeProcessPinTests {
  static Dictionary<string,object> Pin(){return new Dictionary<string,object>{{"processId",101},{"processStartedUtc","2026-10-08T08:38:27.4633424Z"},{"windowsSessionId",1},{"executablePath",@"C:\Program Files\Autodesk\PowerShape 2023\sys\exec64\powershape.exe"}};}
  internal static int Run(){if(!File.Exists(Path.Combine(Program.Root,".process-pin-fixture")))throw new InvalidOperationException("Explicit isolated process-pin marker required; never COM.");int cases=0;
   var p=Pin();var expected=PowerShapeProcessPin.Definition(p);PowerShapeProcessPin.Match(expected,101,101,1,101,"powershape",expected.StartedUtc,1,expected.Executable);cases++;
   foreach(string fault in new[]{"com_pid","window_pid","process_pid","session","current_session","start","name","path"}){
    bool rejected=false;try{PowerShapeProcessPin.Match(expected,fault=="com_pid"?202:101,fault=="window_pid"?202u:101u,fault=="current_session"?2:1,fault=="process_pid"?202:101,fault=="name"?"other":"powershape",fault=="start"?expected.StartedUtc.AddTicks(1):expected.StartedUtc,fault=="session"?2:1,fault=="path"?@"D:\Other\powershape.exe":expected.Executable);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidOperationException("Pinned identity accepted "+fault);cases++;
   }
   foreach(string fault in new[]{"pid_bool","session_string","missing","extra","time_nozone","path_relative","legacy"}){
    var v=Pin();if(fault=="pid_bool")v["processId"]=true;if(fault=="session_string")v["windowsSessionId"]="1";if(fault=="missing")v.Remove("processStartedUtc");if(fault=="extra")v["fallback"]=true;if(fault=="time_nozone")v["processStartedUtc"]="2026-10-08T08:38:27";if(fault=="path_relative")v["executablePath"]="powershape.exe";bool rejected=false;try{PowerShapeProcessPin.ValidateConfiguration(v,fault!="legacy");}catch(Exception){rejected=true;}if(!rejected)throw new InvalidOperationException("Pinned config accepted "+fault);cases++;
   }
   PowerShapeProcessPin.ValidateConfiguration(null,false);PowerShapeProcessPin.Check(null,null);cases++;Directory.CreateDirectory(Path.Combine(Program.Root,"reports"));NativeJournal.Freeze(Path.Combine(Program.Root,"reports","process-pin-tests.json"),new{state="passed",cases,cadConnectionAttempted=false,policy="exact PID/start/session/executable plus COM HWND owner; no fallback"});Console.WriteLine("PASS: "+cases+" exact process pin identity/config/null-default guards; no COM/CAD.");return cases;
  }
 }
}
