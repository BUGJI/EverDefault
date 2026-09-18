using System;
using System.IO;
using Newtonsoft.Json;

namespace EverDefault.Persistence.Json
{
    /// <summary>Atomic JSON file read/write with a size limit aware serializer.</summary>
    internal static class JsonFile
    {
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<T>(json, Serializer.Settings);
        }

        public static void Write<T>(string path, T value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(value, Formatting.Indented, Serializer.Settings);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }
    }
}
