using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace XrayLauncher
{
    public partial class App : Application
    {
        // Centralized version - change here to update everywhere
        public static readonly string Version = "2.4.0";
        public static readonly string AppName = "LX VPN";
        
        // Mutex for single instance
        private static Mutex _mutex;
        private const string MutexName = "LX_VPN_SingleInstance_Mutex";
        
        protected override void OnStartup(StartupEventArgs e)
        {
            // Kill any orphaned xray.exe processes on startup
            KillOrphanedXray();
            
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);
            
            if (!createdNew)
            {
                // Another instance is already running - show beautiful dialog
                var dialog = new AlreadyRunningDialog();
                dialog.ShowDialog();
                Shutdown();
                return;
            }
            
            base.OnStartup(e);
        }
        
        private void KillOrphanedXray()
        {
            try
            {
                Process[] xrayProcesses = Process.GetProcessesByName("xray");
                foreach (Process proc in xrayProcesses)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                    catch { }
                }
            }
            catch { }
        }
        
        protected override void OnExit(ExitEventArgs e)
        {
            // Also kill xray on exit
            KillOrphanedXray();
            
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
