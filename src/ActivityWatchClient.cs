using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AwWatcherNPP
{
    public sealed class ActivityWatchClient : IDisposable
    {
        public const string ClientName = "aw-watcher-notepadpp";
        private readonly HttpClient http;
        private readonly string bucketPath;
        private readonly string hostname;
        private readonly Settings settings;
        private bool bucketReady;

        public ActivityWatchClient(Settings settings, string hostname, HttpMessageHandler handler = null)
        {
            this.settings = settings;
            this.hostname = hostname;
            // Bypass corporate/system proxies and never follow redirects away from loopback.
            http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            http.BaseAddress = new Uri("http://127.0.0.1:" + settings.EffectivePort + "/api/0/");
            http.Timeout = TimeSpan.FromSeconds(2);
            bucketPath = "buckets/" + Uri.EscapeDataString(ClientName + "_" + hostname);
        }

        private async Task<HttpStatusCode> Post(string path, object data, CancellationToken cancellation)
        {
            var json = new JavaScriptSerializer().Serialize(data);
            using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync(path, content, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotModified &&
                    response.StatusCode != HttpStatusCode.NotFound)
                    throw new HttpRequestException("ActivityWatch HTTP " + (int)response.StatusCode);
                return response.StatusCode;
            }
        }

        public async Task Send(EditorActivity activity, CancellationToken cancellation)
        {
            if (!bucketReady)
            {
                var status = await Post(bucketPath, new {
                    client = ClientName, hostname, type = "app.editor.activity"
                }, cancellation).ConfigureAwait(false);
                if (status == HttpStatusCode.NotFound) throw new HttpRequestException("Bucket endpoint not found.");
                bucketReady = true;
            }
            var heartbeat = new {
                timestamp = activity.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                duration = 0,
                data = new { file = activity.File, project = ProjectResolver.Resolve(activity.File),
                    language = activity.Language }
            };
            var result = await Post(bucketPath + "/heartbeat?pulsetime=" + settings.PulseTimeSeconds,
                heartbeat, cancellation).ConfigureAwait(false);
            if (result == HttpStatusCode.NotFound)
            {
                bucketReady = false; // Server restarted or the bucket was removed; retry this event.
                throw new HttpRequestException("Bucket missing; will recreate it.");
            }
            if (result == HttpStatusCode.NotModified)
                throw new HttpRequestException("Unexpected heartbeat response: 304.");
        }

        public void Dispose() { http.Dispose(); }
    }

    // One worker preserves event order, including across connection failures.
    public sealed class HeartbeatWorker : IDisposable
    {
        private readonly Queue<EditorActivity> pending = new Queue<EditorActivity>();
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly ActivityWatchClient client;
        private readonly Thread thread;
        private readonly int capacity;
        private readonly Action<string> report;
        private bool disposed;
        public int Dropped { get; private set; }
        public int PendingCount { get { lock (gate) return pending.Count; } }

        public HeartbeatWorker(ActivityWatchClient client, int capacity, Action<string> report)
        {
            this.client = client;
            this.capacity = capacity;
            this.report = report;
            thread = new Thread(Run) { IsBackground = true, Name = "ActivityWatch heartbeat sender" };
            thread.Start();
        }

        public void Enqueue(EditorActivity activity)
        {
            lock (gate)
            {
                if (disposed) return;
                if (pending.Count >= capacity)
                {
                    // Keep the oldest (possibly in flight) event and reject newest on overflow.
                    Dropped++;
                    return;
                }
                pending.Enqueue(activity);
                wake.Set();
            }
        }

        private void Run()
        {
            var retrySeconds = 1;
            var recovering = false;
            while (!stop.IsCancellationRequested)
            {
                EditorActivity next;
                lock (gate) next = pending.Count == 0 ? null : pending.Peek();
                if (next == null) { wake.WaitOne(1000); continue; }
                try
                {
                    client.Send(next, stop.Token).GetAwaiter().GetResult();
                    lock (gate) pending.Dequeue();
                    if (recovering) report("ActivityWatch connection restored; draining queued heartbeats.");
                    recovering = false;
                    retrySeconds = 1;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException)
                {
                    if (stop.IsCancellationRequested) break;
                    recovering = true;
                    report("ActivityWatch unavailable; queued=" + PendingCount + "; retry in " + retrySeconds + "s.");
                    stop.Token.WaitHandle.WaitOne(retrySeconds * 1000);
                    retrySeconds = Math.Min(30, retrySeconds * 2);
                }
            }
        }

        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; }
            stop.Cancel();
            wake.Set();
            thread.Join(); // In-flight async HTTP is cancelled; no UI-thread HTTP flush.
            client.Dispose();
            wake.Dispose();
            stop.Dispose();
        }
    }
}
