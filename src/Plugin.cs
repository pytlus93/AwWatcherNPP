// Adapted plugin integration; see LICENSE.txt for provenance and license terms.
// Copyright (c) 2014 Alan Hamlett. Copyright (c) 2026 Filip Vrba <pytlus93@seznam.cz>.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using WakaTime; // Pinned DllExport attribute namespace.

namespace AwWatcherNPP
{
    [StructLayout(LayoutKind.Sequential)]
    public struct NppData { public IntPtr Window, MainEditor, SecondEditor; }

    [StructLayout(LayoutKind.Sequential)]
    public struct NotificationHeader { public IntPtr Window; public UIntPtr Id; public uint Code; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void MenuCommand();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct FunctionItem
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        public IntPtr Function;
        public int CommandId;
        [MarshalAs(UnmanagedType.I1)] public bool Checked;
        public IntPtr Shortcut;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LastInput { public uint Size, Tick; }

    public static class Plugin
    {
        private const uint NppMessage = 1024 + 1000;
        private static NppData host;
        private static readonly IntPtr name = Marshal.StringToHGlobalUni("ActivityWatch");
        private static readonly MenuCommand[] commands = { EditSettings, OpenDashboard, ShowStatus };
        private static IntPtr functions;
        private static Timer timer;
        private static HeartbeatWorker worker;
        private static ActivityTracker tracker;
        private static Settings settings;
        private static string configPath;
        private static string status = "Not initialized.";
        private static readonly Stopwatch elapsed = Stopwatch.StartNew();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, StringBuilder text);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessageInt(IntPtr window, uint message, IntPtr wParam, out int value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput input);

        // Use the official C ABI (cdecl); x86 calling convention matters.
        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static bool isUnicode() { return true; }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr getName() { return name; }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static void setInfo(NppData data) { host = data; }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr getFuncsArray(ref int count)
        {
            try
            {
                if (functions == IntPtr.Zero)
                {
                    var labels = new[] { "Settings...", "Open ActivityWatch", "Status" };
                    var size = Marshal.SizeOf(typeof(FunctionItem));
                    functions = Marshal.AllocHGlobal(size * commands.Length);
                    for (int i = 0; i < commands.Length; i++)
                        Marshal.StructureToPtr(new FunctionItem { Name = labels[i],
                            Function = Marshal.GetFunctionPointerForDelegate(commands[i]) },
                            IntPtr.Add(functions, i * size), false);
                }
                count = commands.Length;
                return functions;
            }
            catch (Exception ex) { Report("Menu initialization failed: " + ex.GetType().Name); count = 0; return IntPtr.Zero; }
        }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static IntPtr messageProc(uint message, UIntPtr wParam, IntPtr lParam) { return new IntPtr(1); }

        [DllExport(CallingConvention = CallingConvention.Cdecl)]
        public static void beNotified(IntPtr notification)
        {
            try
            {
                if (notification == IntPtr.Zero) return;
                var header = (NotificationHeader)Marshal.PtrToStructure(notification, typeof(NotificationHeader));
                switch (header.Code)
                {
                    case 1001: Initialize(); break; // NPPN_READY
                    case 1009: Shutdown(); break; // NPPN_SHUTDOWN
                    case 1008: // NPPN_FILESAVED
                    case 1010: // NPPN_BUFFERACTIVATED
                    case 1011: // NPPN_LANGCHANGED
                    case 2001: // SCN_CHARADDED
                    case 2007: // SCN_UPDATEUI: caret movement/scroll/read activity
                    case 2008: // SCN_MODIFIED: includes deletes, not just inserted text
                        Sample(); break;
                }
            }
            catch (Exception ex) { ResetTracking(); Report("Plugin notification failed: " + ex.GetType().Name); }
        }

        private static void Initialize()
        {
            if (worker != null) return;
            EnsureConfigPath();
            ApplySettings(Settings.Load(configPath)); // invalid config disables tracking; never silently resets it
        }

        private static void EnsureConfigPath()
        {
            if (configPath != null) return;
            var directory = new StringBuilder(32768);
            SendMessage(host.Window, NppMessage + 46, new IntPtr(directory.Capacity), directory);
            var root = directory.Length > 0 ? directory.ToString() :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AwWatcherNPP");
            configPath = Path.Combine(root, "AwWatcherNPP.json");
        }

        private static void ApplySettings(Settings value)
        {
            value.Validate();
            timer?.Stop();
            var pending = worker?.TakePendingAndDispose();
            worker = null;
            settings = value;
            tracker = new ActivityTracker(value);
            if (!value.Enabled)
            {
                status = "Tracking stopped by settings; pending observations discarded.";
                return;
            }
            var client = new ActivityWatchClient(value, Environment.MachineName);
            worker = new HeartbeatWorker(client, value.QueueCapacity, Report, pending);
            settings = value;
            tracker = new ActivityTracker(value);
            if (timer == null)
            {
                timer = new Timer { Interval = 1000 };
                timer.Tick += Tick;
            }
            timer.Start();
            status = "Tracking enabled on loopback port " + value.EffectivePort + ".";
            Sample();
        }

        private static void Tick(object sender, EventArgs args)
        {
            try { Sample(); }
            catch (Exception ex) { ResetTracking(); Report("Sampling failed: " + ex.GetType().Name); }
        }

        private static void ResetTracking()
        {
            tracker?.Observe(null, null, false, DateTimeOffset.UtcNow, elapsed.ElapsedMilliseconds);
        }

        private static void Sample()
        {
            if (worker == null) return;
            var input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            var active = GetForegroundWindow() == host.Window && GetLastInputInfo(ref input) &&
                unchecked((uint)Environment.TickCount - input.Tick) < settings.IdleSeconds * 1000u;
            if (!active) { tracker.Observe(null, null, false, DateTimeOffset.UtcNow, elapsed.ElapsedMilliseconds); return; }
            var file = new StringBuilder(32768);
            SendMessage(host.Window, 1024 + 3000 + 1, new IntPtr(file.Capacity), file);
            // Some host versions return no full path for a new buffer; use its tab name.
            if (file.Length == 0)
                SendMessage(host.Window, 1024 + 3000 + 3, new IntPtr(file.Capacity), file);
            int language;
            SendMessageInt(host.Window, NppMessage + 5, IntPtr.Zero, out language);
            var languageName = new StringBuilder(256);
            SendMessage(host.Window, NppMessage + 83, new IntPtr(language), languageName);
            var activity = tracker.Observe(file.ToString(), languageName.Length == 0 ? "Text" : languageName.ToString(),
                true, DateTimeOffset.UtcNow, elapsed.ElapsedMilliseconds);
            if (activity != null) worker.Enqueue(activity);
        }

        private static void Report(string message)
        {
            status = message;
            try
            {
                if (configPath != null)
                {
                    var log = Path.ChangeExtension(configPath, ".log");
                    // Bound logs, and never log file/project names or document contents.
                    if (File.Exists(log) && new FileInfo(log).Length > 128 * 1024) File.WriteAllText(log, "");
                    File.AppendAllText(log, DateTimeOffset.UtcNow.ToString("o") + " " + message + Environment.NewLine);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Shutdown()
        {
            timer?.Stop();
            timer?.Dispose();
            timer = null;
            worker?.Dispose();
            worker = null;
            // Name/menu pointers remain valid for any final host calls; the process owns their lifetime.
        }

        private static void EditSettings()
        {
            try
            {
                EnsureConfigPath();
                var current = settings ?? new Settings();
                string warning = null;
                try { current = Settings.Load(configPath); }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    warning = "Saved settings could not be loaded. Review all values before saving.";
                }
                using (var dialog = new SettingsForm(current, value => {
                    value.Save(configPath);
                    ApplySettings(value);
                }, warning))
                    dialog.ShowDialog(new HostWindow(host.Window));
            }
            catch (Exception ex) { Report("Cannot open settings: " + ex.GetType().Name); }
        }

        private sealed class HostWindow : IWin32Window
        {
            public IntPtr Handle { get; }
            public HostWindow(IntPtr handle) { Handle = handle; }
        }

        private static void OpenDashboard()
        {
            try { Process.Start("http://127.0.0.1:" + (settings?.EffectivePort ?? 5600)); }
            catch (Exception ex) { Report("Cannot open dashboard: " + ex.GetType().Name); }
        }

        private static void ShowStatus()
        {
            MessageBox.Show(status + Environment.NewLine + "Queued: " + (worker?.PendingCount ?? 0) +
                "; dropped: " + (worker?.Dropped ?? 0) + Environment.NewLine + "Config: " + configPath,
                "ActivityWatch watcher");
        }
    }
}
