using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string mutexName = "AudioAuditor_SingleInstance_F7A3B2";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running — bring it to front
                BringExistingInstanceToFront();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            ThemeManager.Initialize();
        }

        private static void BringExistingInstanceToFront()
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
            {
                if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
                {
                    if (IsIconic(proc.MainWindowHandle))
                        ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(proc.MainWindowHandle);
                    break;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
