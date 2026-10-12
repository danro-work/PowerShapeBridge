using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal static class Launcher
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=0)return 64;
        string root=AppDomain.CurrentDomain.BaseDirectory;
        string release=Path.Combine(root,"releases","P2.6");
        try
        {
            foreach(string file in new[]{"PSBridge.exe","PSBridge.Worker.exe","bridge.config.json","mcp_server.py"})
                if(!File.Exists(Path.Combine(release,file)))throw new FileNotFoundException("Отсутствует файл текущей версии: "+file);
            foreach(Process existing in Process.GetProcessesByName("PSBridge"))
            {
                using(existing)
                {
                    if(existing.Id==Process.GetCurrentProcess().Id||existing.SessionId!=Process.GetCurrentProcess().SessionId)continue;
                    try { if(string.Equals(existing.MainModule.FileName,Path.Combine(release,"PSBridge.exe"),StringComparison.OrdinalIgnoreCase))return 0; }
                    catch(System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Не удалось проверить уже запущенную панель Bridge. Закройте её перед новым запуском."); }
                }
            }
            Process.Start(new ProcessStartInfo(Path.Combine(release,"PSBridge.exe")) { WorkingDirectory=release,UseShellExecute=true });
            return 0;
        }
        catch(Exception error)
        {
            MessageBox.Show("Не удалось открыть текущую версию Bridge.\r\n"+error.Message,"PowerShape Bridge",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 3;
        }
    }
}
