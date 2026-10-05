using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AwWatcherNPP
{
    public sealed class Settings
    {
        public int Port { get; set; } = 5600;
        public bool Testing { get; set; }
        public int FrequencySeconds { get; set; } = 5;
        public int PulseTimeSeconds { get; set; } = 20;
        public int IdleSeconds { get; set; } = 180;
        public int QueueCapacity { get; set; } = 1000;
        public Dictionary<string, string> Languages { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public int EffectivePort => Testing && Port == 5600 ? 5666 : Port;

        public void Validate()
        {
            if (Port < 1 || Port > 65535) throw new ArgumentException("Port must be 1–65535.");
            if (FrequencySeconds < 1 || FrequencySeconds > 60)
                throw new ArgumentException("FrequencySeconds must be 1–60.");
            if (PulseTimeSeconds < FrequencySeconds || PulseTimeSeconds > 120)
                throw new ArgumentException("PulseTimeSeconds must be between FrequencySeconds and 120.");
            if (IdleSeconds < FrequencySeconds || IdleSeconds > 3600)
                throw new ArgumentException("IdleSeconds must be between FrequencySeconds and 3600.");
            if (QueueCapacity < 1 || QueueCapacity > 10000)
                throw new ArgumentException("QueueCapacity must be 1–10000.");
            Languages = BuildLanguages(Languages ?? new Dictionary<string, string>());
        }

        public static Dictionary<string, string> BuildLanguages(IEnumerable<KeyValuePair<string, string>> rows)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int row = 0;
            foreach (var mapping in rows)
            {
                row++;
                var extension = (mapping.Key ?? "").Trim();
                var language = (mapping.Value ?? "").Trim();
                if (extension.Length == 0 && language.Length == 0) continue;
                if (extension.StartsWith(".", StringComparison.Ordinal)) extension = extension.Substring(1);
                if (extension.Length == 0 || language.Length == 0)
                    throw new ArgumentException("Language row " + row + ": enter both an extension and a language.");
                if (extension.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || ".\\/:*?\"<>|".Contains(c)))
                    throw new ArgumentException("Language row " + row + ": use one extension, such as py, without wildcards or paths.");
                if (result.ContainsKey(extension))
                    throw new ArgumentException("Language row " + row + ": extension '" + extension + "' is already listed.");
                result.Add(extension.ToLowerInvariant(), language);
            }
            return result;
        }

        public void Save(string path)
        {
            Validate();
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static Settings Load(string path)
        {
            var json = new JavaScriptSerializer();
            if (!File.Exists(path))
            {
                new Settings().Save(path);
            }
            var settings = json.Deserialize<Settings>(File.ReadAllText(path));
            if (settings == null) throw new ArgumentException("Settings must be a JSON object.");
            settings.Validate();
            return settings;
        }
    }
}
