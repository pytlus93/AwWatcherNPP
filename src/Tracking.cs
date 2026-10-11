using System;
using System.Collections.Generic;
using System.IO;

namespace AwWatcherNPP
{
    public sealed class EditorActivity
    {
        public DateTimeOffset Timestamp { get; }
        public bool StartsInterval { get; }
        public string File { get; }
        public string Language { get; }

        public EditorActivity(DateTimeOffset timestamp, string file, string language, bool startsInterval = false)
        {
            Timestamp = timestamp;
            StartsInterval = startsInterval;
            File = file;
            Language = language ?? "Text";
        }
    }

    // Host-independent throttling. No IDE handles or HTTP calls belong here.
    public sealed class ActivityTracker
    {
        private readonly Settings settings;
        private EditorActivity last;
        private long lastTick;

        public ActivityTracker(Settings settings) { this.settings = settings; }

        public EditorActivity Observe(string file, string language, bool active,
            DateTimeOffset timestamp, long monotonicMilliseconds)
        {
            if (!active || string.IsNullOrEmpty(file)) { last = null; return null; }
            string custom;
            if (settings.Languages.TryGetValue(Path.GetExtension(file).TrimStart('.'), out custom))
                language = custom;
            if (last != null && last.File == file && last.Language == language &&
                monotonicMilliseconds - lastTick < settings.FrequencySeconds * 1000L)
                return null;
            last = new EditorActivity(timestamp, file, language, last == null ||
                monotonicMilliseconds - lastTick > (settings.FrequencySeconds + 1) * 1000L);
            lastTick = monotonicMilliseconds;
            return last;
        }
    }

    public static class ProjectResolver
    {
        // Like editor watchers, use the nearest VCS root; .git can be a worktree file.
        // Directory fallback avoids guessing a case or customer from a path.
        public static string Resolve(string file)
        {
            try
            {
                if (!Path.IsPathRooted(file)) return ""; // unsaved buffers
                var start = new DirectoryInfo(Path.GetDirectoryName(file));
                for (var directory = start; directory != null; directory = directory.Parent)
                {
                    foreach (var marker in new[] { ".git", ".hg", ".svn" })
                    {
                        var path = Path.Combine(directory.FullName, marker);
                        if (Directory.Exists(path) || System.IO.File.Exists(path)) return directory.FullName;
                    }
                }
                return start.FullName;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is System.Security.SecurityException)
            {
                return "";
            }
        }
    }
}
