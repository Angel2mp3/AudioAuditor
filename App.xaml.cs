using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AudioQualityChecker.Services;

namespace AudioQualityChecker
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        private static bool _createdNewMutex;

        /// <summary>
        /// File/folder paths captured from the command line at startup.
        /// MainWindow consumes these once it's loaded.
        /// </summary>
        public static IReadOnlyList<string> PendingStartupPaths { get; private set; } = Array.Empty<string>();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref CopyDataStruct lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct CopyDataStruct
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        private const int SW_RESTORE = 9;
        private const int WM_COPYDATA = 0x004A;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Capture file/folder args from "Open With" or drag-onto-exe launches
            var paths = (e.Args ?? Array.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("-"))
                .ToArray();

            // Did a fatal crash relaunch us? (marker arg is filtered out of `paths`
            // above because it starts with '-'.)
            bool isCrashRelaunch = (e.Args ?? Array.Empty<string>())
                .Any(a => string.Equals(a, RelaunchMarkerArg, StringComparison.OrdinalIgnoreCase));

            const string mutexName = "AudioAuditor_SingleInstance_F7A3B2";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
            _createdNewMutex = createdNew;

            if (!createdNew && isCrashRelaunch)
            {
                // The crashing instance that spawned us may still be tearing down and
                // briefly holding the mutex. Wait for it to release (or be abandoned when
                // it dies) instead of exiting — otherwise the relaunch silently no-ops.
                try
                {
                    if (_singleInstanceMutex.WaitOne(TimeSpan.FromSeconds(5)))
                    {
                        createdNew = true;        // we now own it — act as the primary instance
                        _createdNewMutex = true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner died without releasing — ownership transfers to us.
                    createdNew = true;
                    _createdNewMutex = true;
                }
            }

            if (!createdNew)
            {
                // Another instance is already running — forward any paths via WM_COPYDATA, then exit.
                ForwardPathsToExistingInstance(paths);
                BringExistingInstanceToFront();
                _singleInstanceMutex?.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            PendingStartupPaths = paths;
            base.OnStartup(e);
            ThemeManager.Initialize();
            ApplyGpuRenderMode();

            // Crash logging — active by default; user can opt out via the first-run
            // prompt (shown by MainWindow) or in Settings. Logs are local-only and
            // path-sanitized (see LocalCrashLogger). Hook all three crash channels so
            // UI-thread and background-task failures are captured, not just AppDomain.
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (ThemeManager.CrashLoggingEnabled && args.ExceptionObject is Exception ex)
                    LocalCrashLogger.Write(ex, "AppDomain (fatal)");
                // This channel is always terminal; leave a breadcrumb so the next launch
                // can show a subtle "recovered from a problem" notice.
                if (args.ExceptionObject is Exception fatal)
                    TrySnapshotAndMarkCrash(fatal);
            };

            DispatcherUnhandledException += (_, args) =>
            {
                if (ThemeManager.CrashLoggingEnabled)
                    LocalCrashLogger.Write(args.Exception, "UI thread (Dispatcher)");

                // Try to recover in place: many UI-thread faults (a binding/render glitch,
                // a click handler that threw) don't corrupt app state. Swallowing them keeps
                // the app alive. A crash-loop guard prevents masking a truly broken state —
                // if we recover too many times in a short window, we stop recovering and let
                // it fail fast (and relaunch) instead. Exceptions that mean the process is
                // already hosed (e.g. out of memory) are never recovered in place.
                if (IsRecoverable(args.Exception) && ShouldAttemptInPlaceRecovery())
                {
                    NoteRecovery();
                    ShowRecoveryNotice();
                    args.Handled = true; // recovered — keep running
                    return;
                }

                // Recovery budget exhausted → treat as fatal: snapshot + relaunch clean.
                TrySnapshotAndMarkCrash(args.Exception);
                RelaunchAfterFatal();
                // Leave Handled = false so the process still terminates.
            };

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                if (ThemeManager.CrashLoggingEnabled)
                    LocalCrashLogger.Write(args.Exception, "Background task (unobserved)");
                // Unobserved task exceptions are non-fatal by default in modern .NET;
                // observing them keeps the process alive without further action.
                args.SetObserved();
            };
        }

        /// <summary>
        /// Applies the user's GPU acceleration choice process-wide. Must run once at
        /// startup; WPF reads ProcessRenderMode when the first window's render target is
        /// created. ForceSoftware pins the CPU rasterizer (for broken/blacklisted GPU
        /// drivers); Auto leaves WPF on its hardware-accelerated Default — there is no
        /// per-process way to *force* the GPU beyond not disabling it.
        /// </summary>
        private static void ApplyGpuRenderMode()
        {
            try
            {
                RenderOptions.ProcessRenderMode = ThemeManager.GpuRenderMode == GpuRenderMode.ForceSoftware
                    ? RenderMode.SoftwareOnly
                    : RenderMode.Default;
            }
            catch { /* never let a rendering preference block startup */ }
        }

        // ── In-place UI-thread crash recovery + crash-loop guard ─────────────

        // If more than this many UI-thread faults are recovered within the window below,
        // stop recovering — repeated faults usually mean genuinely broken state.
        private const int RecoveryBudget = 3;
        private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(20);
        private static readonly System.Collections.Generic.Queue<DateTime> _recentRecoveries = new();
        private static bool _recoveryNoticeShownThisRun;

        /// <summary>
        /// Some exceptions mean the process is already in an unrecoverable state — swallowing
        /// them just hides corruption. Let those fall through to the fatal relaunch path.
        /// </summary>
        private static bool IsRecoverable(Exception? ex)
        {
            return ex is not (OutOfMemoryException
                              or StackOverflowException   // (not catchable here, but explicit)
                              or AccessViolationException
                              or System.Runtime.InteropServices.SEHException);
        }

        private static bool ShouldAttemptInPlaceRecovery()
        {
            // Prune entries older than the window, then check if we're still under budget.
            var now = DateTime.UtcNow;
            while (_recentRecoveries.Count > 0 && now - _recentRecoveries.Peek() > RecoveryWindow)
                _recentRecoveries.Dequeue();
            return _recentRecoveries.Count < RecoveryBudget;
        }

        private static void NoteRecovery() => _recentRecoveries.Enqueue(DateTime.UtcNow);

        private static void ShowRecoveryNotice()
        {
            try
            {
                if (_recoveryNoticeShownThisRun) return;
                _recoveryNoticeShownThisRun = true;
                if (Current?.MainWindow is MainWindow mw)
                {
                    mw.ShowThemedNotice(
                        "Recovered from a problem",
                        "AudioAuditor hit an unexpected error but kept running. If anything looks off, restarting is the safest fix.");
                }
            }
            catch { /* a notice must never escalate a crash */ }
        }

        /// <summary>Saves a session snapshot and drops a recovery breadcrumb for next launch.</summary>
        private static void TrySnapshotAndMarkCrash(Exception ex)
        {
            try
            {
                if (Current?.MainWindow is MainWindow mw)
                    mw.SaveSessionState(crashSnapshot: true);
            }
            catch { }
            try { SessionRestoreService.WriteRecoveryMarker(ex?.GetType().Name ?? "crash"); }
            catch { }
        }

        /// <summary>
        /// Relaunches the app once after a fatal crash, passing a marker arg and guarding
        /// against a relaunch loop (don't relaunch if we ourselves were just relaunched).
        /// </summary>
        private static void RelaunchAfterFatal()
        {
            try
            {
                bool wasRelaunched = (Environment.GetCommandLineArgs() ?? Array.Empty<string>())
                    .Any(a => string.Equals(a, RelaunchMarkerArg, StringComparison.OrdinalIgnoreCase));
                if (wasRelaunched) return; // already a relaunch — don't loop

                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath)) return;

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = RelaunchMarkerArg,
                    UseShellExecute = true,
                });
            }
            catch { /* best effort — never throw from a crash path */ }
        }

        // Command-line marker telling a relaunched instance it came from a crash.
        public const string RelaunchMarkerArg = "--recovered-from-crash";

        private static void ForwardPathsToExistingInstance(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            try
            {
                var hwnd = FindExistingMainWindowHandle();
                if (hwnd == IntPtr.Zero) return;

                string payload = string.Join("\n", paths);
                IntPtr buffer = Marshal.StringToHGlobalUni(payload);
                try
                {
                    var cds = new CopyDataStruct
                    {
                        dwData = (IntPtr)AudioQualityChecker.MainWindow.OpenPathsCopyDataId,
                        cbData = (payload.Length + 1) * 2, // UTF-16 bytes including terminator
                        lpData = buffer
                    };
                    SendMessage(hwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { }
        }

        private static IntPtr FindExistingMainWindowHandle()
        {
            var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var proc in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
            {
                if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
                    return proc.MainWindowHandle;
            }
            return IntPtr.Zero;
        }

        private static void BringExistingInstanceToFront()
        {
            var hwnd = FindExistingMainWindowHandle();
            if (hwnd == IntPtr.Zero) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_createdNewMutex && _singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
