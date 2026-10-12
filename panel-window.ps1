# Only the deployment script may request close, after process/path/start/session checks.
function Initialize-BridgePanelWindowApi {
    if ('BridgeDeployment.PanelWindows' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
namespace BridgeDeployment {
 public sealed class PanelWindow {
  public long Handle; public int ProcessId; public string Title; public string ClassName; public bool Visible;
 }
 public static class PanelWindows {
  delegate bool Callback(IntPtr hwnd,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr parameter);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
  public static PanelWindow[] Read(int processId) {
   var windows=new List<PanelWindow>();
   bool enumerated=EnumWindows((hwnd,parameter)=>{
    uint owner;GetWindowThreadProcessId(hwnd,out owner);if(owner!=(uint)processId)return true;
    var title=new StringBuilder(1024);var type=new StringBuilder(256);GetWindowText(hwnd,title,title.Capacity);GetClassName(hwnd,type,type.Capacity);
    windows.Add(new PanelWindow{Handle=hwnd.ToInt64(),ProcessId=processId,Title=title.ToString(),ClassName=type.ToString(),Visible=IsWindowVisible(hwnd)});return true;
   },IntPtr.Zero);
   if(!enumerated)throw new InvalidOperationException("Window enumeration failed.");return windows.ToArray();
  }
  public static bool RequestClose(long handle,int processId) {
   // Recheck owner, class and exact handle; only the Bridge main form qualifies.
   var matches=new List<PanelWindow>();
   foreach(var window in Read(processId))if(window.Title.StartsWith("PowerShape Bridge",StringComparison.Ordinal)&&window.ClassName.StartsWith("WindowsForms10.Window.",StringComparison.Ordinal))matches.Add(window);
   if(matches.Count!=1||matches[0].Handle!=handle)throw new InvalidOperationException("Known panel window changed or is ambiguous.");
   return PostMessage(new IntPtr(handle),0x0010,IntPtr.Zero,IntPtr.Zero);
  }
 }
}
'@
}
function Get-BridgePanelWindows([int]$ProcessId) {
    Initialize-BridgePanelWindowApi
    return [BridgeDeployment.PanelWindows]::Read($ProcessId)
}
function Close-BridgePanelGracefully([Diagnostics.Process]$Panel) {
    $windows = @(Get-BridgePanelWindows $Panel.Id | Where-Object { $_.Title.StartsWith('PowerShape Bridge') -and $_.ClassName.StartsWith('WindowsForms10.Window.') })
    if ($windows.Count -ne 1) { throw 'Known Bridge main form is missing or ambiguous; no close sent.' }
    if (![BridgeDeployment.PanelWindows]::RequestClose($windows[0].Handle,$Panel.Id) -or !$Panel.WaitForExit(10000)) { throw 'Known Bridge panel did not close cleanly; no forced termination performed.' }
    return $windows[0]
}
