using System;
using System.Collections.Generic;
using System.IO;
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
            Languages = new Dictionary<string, string>(Languages ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public static Settings Load(string path)
        {
            var json = new JavaScriptSerializer();
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json.Serialize(new Settings()));
            }
            var settings = json.Deserialize<Settings>(File.ReadAllText(path));
            if (settings == null) throw new ArgumentException("Settings must be a JSON object.");
            settings.Validate();
            return settings;
        }
    }
}
