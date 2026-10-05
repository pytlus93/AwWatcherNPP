using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AwWatcherNPP
{
    internal class Tests
    {
        private static int passed;
        private static readonly DateTimeOffset time = DateTimeOffset.Parse("2026-10-05T10:00:00+02:00");

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            passed++;
        }

        private static void Throws(Action action, string message)
        {
            try { action(); }
            catch (ArgumentException) { passed++; return; }
            throw new Exception(message);
        }

        private static void Wait(Func<bool> predicate, string message)
        {
            var clock = Stopwatch.StartNew();
            while (!predicate() && clock.ElapsedMilliseconds < 5000) Thread.Sleep(10);
            Check(predicate(), message);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                SettingsAndTracking();
                Projects();
                Protocol().GetAwaiter().GetResult();
                WorkerRecovery();
                LoopbackProtocol();
                ExportSmoke(args[0]);
                SettingsDialog();
                if (args.Length == 3 && args[1] == "--preview") RenderSettings(args[2]);
                Console.WriteLine("PASS: " + passed + " assertions, including native DLL exports.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void SettingsAndTracking()
        {
            Check(new Settings { Testing = true }.EffectivePort == 5666, "Debug port");
            Check(new Settings { Testing = true, Port = 1234 }.EffectivePort == 1234, "Explicit port");
            Throws(() => new Settings { FrequencySeconds = 0 }.Validate(), "Invalid frequency accepted");
            Throws(() => new Settings { PulseTimeSeconds = 2 }.Validate(), "Invalid pulse accepted");
            Throws(() => new Settings { QueueCapacity = 0 }.Validate(), "Unbounded queue accepted");
            var settings = new Settings { Languages = new Dictionary<string, string> { ["TPL"] = "Template" } };
            settings.Validate();
            var tracker = new ActivityTracker(settings);
            Check(tracker.Observe("a.tpl", "Text", true, time, 0).Language == "Template", "Extension override");
            Check(tracker.Observe("a.tpl", "Text", true, time, 4999) == null, "Throttle");
            Check(tracker.Observe("a.tpl", "Text", true, time, 5000) != null, "Boundary heartbeat");
            Check(tracker.Observe("b.py", "Python", true, time, 5001) != null, "File switch delayed");
            Check(tracker.Observe("b.py", "Text", true, time, 5002) != null, "Language switch delayed");
            Check(tracker.Observe("b.py", "Text", false, time, 5003) == null, "Background tracked");
            Check(tracker.Observe("b.py", "Text", true, time, 5004) != null, "Focus return delayed");
            Check(tracker.Observe("", "Text", true, time, 5005) == null, "Empty file tracked");
            var rows = new[] { new KeyValuePair<string, string>(" .PY ", " Python "),
                new KeyValuePair<string, string>("", "") };
            Check(Settings.BuildLanguages(rows)["py"] == "Python", "Trim/leading-dot normalization");
            Throws(() => Settings.BuildLanguages(new[] { rows[0], new KeyValuePair<string, string>("py", "Other") }),
                "Duplicate extensions accepted");
            Throws(() => Settings.BuildLanguages(new[] { new KeyValuePair<string, string>("*.py", "Python") }),
                "Wildcard extension accepted");
            Throws(() => Settings.BuildLanguages(new[] { new KeyValuePair<string, string>("tar.gz", "Archive") }),
                "Compound extension accepted");
            Throws(() => Settings.BuildLanguages(new[] { new KeyValuePair<string, string>("py", "") }),
                "Missing language accepted");
        }

        private static void SettingsDialog()
        {
            var original = new Settings { Languages = new Dictionary<string, string> { ["py"] = "Python" } };
            Settings saved = null;
            using (var form = new SettingsForm(original, value => saved = value))
            {
                form.LanguageTable.Rows.Add(" .TPL ", " Template ");
                Check(form.TrySave(), "Valid UI settings rejected");
                Check(saved.Languages["tpl"] == "Template", "UI mapping not saved");
                Check(original.Languages.Count == 1, "Editing mutated running settings");
                form.LanguageTable.Rows.Add("PY", "Duplicate");
                Check(!form.TrySave() && form.ErrorMessage.Contains("already listed"), "Duplicate UI row did not show validation error");
                Check(saved.Languages.Count == 2, "Invalid save reached callback");
                form.LanguageTable.Rows.RemoveAt(2);
                form.LanguageTable.Rows.Add("md", "");
                Check(!form.TrySave() && form.ErrorMessage.Contains("enter both"), "Incomplete UI row accepted");
            }
            var callbackCount = 0;
            using (var form = new SettingsForm(original, value => callbackCount++)) form.Close();
            Check(callbackCount == 0, "Cancel saved settings");
            using (var form = new SettingsForm(original, value => { throw new IOException("locked"); }))
                Check(!form.TrySave() && form.ErrorMessage.Contains("writable"), "Save failure not reported in UI");
        }

        private static void RenderSettings(string path)
        {
            using (var form = new SettingsForm(new Settings { Languages = new Dictionary<string, string> {
                ["tpl"] = "Template", ["mdx"] = "Markdown" } }, _ => { }))
            {
                // Load native controls offscreen, without taking focus or appearing in the taskbar.
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-10000, -10000);
                form.Opacity = 0;
                form.Show();
                Application.DoEvents();
                foreach (var button in Descendants(form).OfType<Button>())
                {
                    var bounds = new Rectangle(form.PointToClient(button.PointToScreen(Point.Empty)), button.Size);
                    Check(button.Visible && form.ClientRectangle.Contains(bounds), "Clipped settings button: " + button.Text);
                }
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                    image.Save(path, ImageFormat.Png);
                }
            }
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }


        private static void Projects()
        {
            var root = Path.Combine(Path.GetTempPath(), "aw-npp-tests-" + Guid.NewGuid());
            try
            {
                var directory = Path.Combine(root, "Repo", "src");
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(root, "Repo", ".git"), "gitdir: ../metadata");
                Check(ProjectResolver.Resolve(Path.Combine(directory, "file.cs")) == "Repo", "Worktree root");
                Directory.CreateDirectory(Path.Combine(directory, ".hg"));
                Check(ProjectResolver.Resolve(Path.Combine(directory, "file.cs")) == "src", "Nearest root");
                Check(ProjectResolver.Resolve("new 1") == "", "Unsaved project");
                var config = Path.Combine(root, "settings.json");
                Check(Settings.Load(config).FrequencySeconds == 5 && File.Exists(config), "Config creation");
                var changed = new Settings { Port = 5666, FrequencySeconds = 10,
                    Languages = new Dictionary<string, string> { [".TPL"] = "Template" } };
                changed.Save(config);
                var reloaded = Settings.Load(config);
                Check(reloaded.Port == 5666 && reloaded.FrequencySeconds == 10 && reloaded.Languages["tpl"] == "Template", "Settings round trip");
                Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Settings temporary file leaked");
                Throws(() => new Settings { Port = 0 }.Save(config), "Invalid settings were saved");
                Check(Settings.Load(config).Port == 5666, "Failed save changed the existing file");
                File.WriteAllText(config, "{\"Port\":0}");
                Throws(() => Settings.Load(config), "Invalid config silently reset");
            }
            finally { Directory.Delete(root, true); } // exact unique test directory created above
        }

        private sealed class FakeServer : HttpMessageHandler
        {
            public readonly List<string> Paths = new List<string>();
            public readonly List<string> Bodies = new List<string>();
            public readonly Queue<HttpStatusCode> Responses = new Queue<HttpStatusCode>();
            public volatile bool Offline;
            public volatile bool CancelBlock;
            public int Attempts;
            public int SuccessfulHeartbeats;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Interlocked.Increment(ref Attempts);
                if (CancelBlock) await Task.Delay(Timeout.Infinite, token);
                if (Offline) throw new HttpRequestException("offline");
                var body = await request.Content.ReadAsStringAsync();
                HttpStatusCode response;
                lock (Paths)
                {
                    Paths.Add(request.RequestUri.AbsoluteUri);
                    Bodies.Add(body);
                    response = Responses.Count > 0 ? Responses.Dequeue() : HttpStatusCode.OK;
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat") && response == HttpStatusCode.OK)
                    Interlocked.Increment(ref SuccessfulHeartbeats);
                return new HttpResponseMessage(response);
            }
        }

        private static async Task Protocol()
        {
            var server = new FakeServer();
            server.Responses.Enqueue(HttpStatusCode.NotModified); // existing bucket accepted
            server.Responses.Enqueue(HttpStatusCode.OK);
            server.Responses.Enqueue(HttpStatusCode.NotFound); // deleted bucket/server restart
            using (var client = new ActivityWatchClient(new Settings { Testing = true }, "Test Host", server))
            {
                var activity = new EditorActivity(time, "new \"č\"\\1", "Text");
                await client.Send(activity, CancellationToken.None);
                var json = new JavaScriptSerializer();
                var bucket = json.Deserialize<Dictionary<string, object>>(server.Bodies[0]);
                Check((string)bucket["type"] == "app.editor.activity", "Bucket type");
                Check(server.Paths[0].Contains("127.0.0.1:5666/api/0/buckets/aw-watcher-notepadpp_Test%20Host"), "Endpoint/bucket encoding");
                Check(server.Paths[1].EndsWith("/heartbeat?pulsetime=20"), "Pulse parameter");
                var heartbeat = json.Deserialize<Dictionary<string, object>>(server.Bodies[1]);
                Check((string)heartbeat["timestamp"] == "2026-10-05T08:00:00.000Z", "UTC timestamp");
                Check(Convert.ToInt32(heartbeat["duration"]) == 0, "Zero duration");
                var data = (Dictionary<string, object>)heartbeat["data"];
                Check((string)data["file"] == activity.File && (string)data["language"] == "Text", "Unicode/JSON escaping");
                try { await client.Send(activity, CancellationToken.None); throw new Exception("404 ignored"); }
                catch (HttpRequestException) { passed++; }
                await client.Send(activity, CancellationToken.None);
                Check(server.Paths[3] == server.Paths[0], "Missing bucket not recreated");
            }
        }

        private static void WorkerRecovery()
        {
            var server = new FakeServer { Offline = true };
            using (var worker = new HeartbeatWorker(new ActivityWatchClient(new Settings(), "test", server), 2, _ => { }))
            {
                worker.Enqueue(new EditorActivity(time, "first", "Text"));
                Wait(() => server.Attempts > 0, "Offline request missing");
                worker.Enqueue(new EditorActivity(time.AddSeconds(5), "second", "Python"));
                worker.Enqueue(new EditorActivity(time.AddSeconds(10), "overflow", "Text"));
                Check(worker.PendingCount == 2 && worker.Dropped == 1, "Queue bound");
                server.Offline = false;
                Wait(() => worker.PendingCount == 0, "Queue did not recover");
                Check(server.SuccessfulHeartbeats == 2, "Heartbeat lost or duplicated");
                Check(server.Bodies[1].Contains("first") && server.Bodies[2].Contains("second"), "Recovery order");
            }
            var blocked = new FakeServer { CancelBlock = true };
            var cancelling = new HeartbeatWorker(new ActivityWatchClient(new Settings(), "test", blocked), 2, _ => { });
            cancelling.Enqueue(new EditorActivity(time, "file", "Text"));
            Wait(() => blocked.Attempts > 0, "Blocking send missing");
            var clock = Stopwatch.StartNew();
            cancelling.Dispose();
            Check(clock.ElapsedMilliseconds < 1000, "Shutdown did not cancel HTTP");

            var oldServer = new FakeServer { Offline = true };
            var previous = new HeartbeatWorker(new ActivityWatchClient(new Settings(), "test", oldServer), 3, _ => { });
            previous.Enqueue(new EditorActivity(time, "before-one", "Text"));
            previous.Enqueue(new EditorActivity(time.AddSeconds(5), "before-two", "Text"));
            Wait(() => oldServer.Attempts > 0, "Old settings worker did not start");
            var backlog = previous.TakePendingAndDispose();
            Check(backlog.Length == 2 && backlog[0].File == "before-one", "Changing settings lost pending events");
            var newServer = new FakeServer { CancelBlock = true };
            using (var updated = new HeartbeatWorker(new ActivityWatchClient(new Settings(), "test", newServer), 1, _ => { }, backlog))
            {
                updated.Enqueue(new EditorActivity(time, "new-overflow", "Text"));
                Check(updated.PendingCount == 2 && updated.Dropped == 1, "Reduced capacity discarded an existing backlog");
            }
        }

        private static void LoopbackProtocol()
        {
            // Real loopback sockets validate HttpClient's wire format and redirect policy.
            // No existing ActivityWatch bucket or user data is touched.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var requests = new List<string>();
            var bodies = new List<string>();
            var server = Task.Run(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    using (var connection = listener.AcceptTcpClient())
                    using (var stream = connection.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        connection.ReceiveTimeout = 5000;
                        requests.Add(reader.ReadLine());
                        var length = 0;
                        string line;
                        while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                length = int.Parse(line.Substring("Content-Length:".Length).Trim());
                        // Test requests are ASCII so byte and character lengths agree.
                        var body = new char[length];
                        int read = 0;
                        while (read < length)
                        {
                            var chunk = reader.Read(body, read, length - read);
                            if (chunk == 0) throw new IOException("Truncated HTTP request.");
                            read += chunk;
                        }
                        bodies.Add(new string(body));
                        var status = i == 0 ? "304 Not Modified" : i == 1 ? "200 OK" : "302 Found";
                        var headers = i == 2 ? "Location: http://127.0.0.1:1/forbidden\r\n" : "";
                        var response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\n" + headers +
                            "Content-Length: 0\r\nConnection: close\r\n\r\n");
                        stream.Write(response, 0, response.Length);
                    }
                }
            });
            try
            {
                using (var client = new ActivityWatchClient(new Settings {
                    Port = ((IPEndPoint)listener.LocalEndpoint).Port }, "socket-test"))
                {
                    var activity = new EditorActivity(time, "new 1", "Text");
                    client.Send(activity, CancellationToken.None).GetAwaiter().GetResult();
                    try { client.Send(activity, CancellationToken.None).GetAwaiter().GetResult();
                        throw new Exception("HTTP redirect accepted"); }
                    catch (HttpRequestException ex) { Check(ex.Message.Contains("302"), "Redirect followed instead of rejected"); }
                }
                Check(server.Wait(5000), "Loopback server incomplete");
                Check(requests[0].StartsWith("POST /api/0/buckets/aw-watcher-notepadpp_socket-test "), "Bucket HTTP method/path");
                Check(requests[1].StartsWith("POST /api/0/buckets/aw-watcher-notepadpp_socket-test/heartbeat?pulsetime=20 "), "Heartbeat HTTP method/path");
                Check(bodies[1].Contains("2026-10-05T08:00:00.000Z"), "Capture timestamp not sent on wire");
            }
            finally { listener.Stop(); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string file);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr library, string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetName();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate bool IsUnicode();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetFunctions(ref int count);

        private static void ExportSmoke(string path)
        {
            var library = LoadLibrary(Path.GetFullPath(path));
            Check(library != IntPtr.Zero, "Native DLL load failed: " + Marshal.GetLastWin32Error());
            foreach (var export in new[] { "getName", "isUnicode", "setInfo", "getFuncsArray", "beNotified", "messageProc" })
                Check(GetProcAddress(library, export) != IntPtr.Zero, "Missing export " + export);
            var getName = (GetName)Marshal.GetDelegateForFunctionPointer(GetProcAddress(library, "getName"), typeof(GetName));
            Check(Marshal.PtrToStringUni(getName()) == "ActivityWatch", "Exported plugin name");
            var unicode = (IsUnicode)Marshal.GetDelegateForFunctionPointer(GetProcAddress(library, "isUnicode"), typeof(IsUnicode));
            Check(unicode(), "Unicode plugin marker");
            var getFunctions = (GetFunctions)Marshal.GetDelegateForFunctionPointer(GetProcAddress(library, "getFuncsArray"), typeof(GetFunctions));
            var count = 0;
            var functions = getFunctions(ref count);
            Check(count == 3 && functions != IntPtr.Zero, "Native menu array");
            Check(Marshal.PtrToStringUni(functions) == "Settings...", "Settings dialog menu entry");
            // Official FuncItem alignment: 152 bytes x64, 144 bytes x86.
            var stride = IntPtr.Size == 8 ? 152 : 144;
            Check(Marshal.PtrToStringUni(IntPtr.Add(functions, stride)) == "Open ActivityWatch", "Menu ABI stride");
            Check(getFunctions(ref count) == functions, "Repeated menu initialization leaked/rebuilt");
        }
    }
}
