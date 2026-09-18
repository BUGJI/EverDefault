using Newtonsoft.Json;

namespace EverDefault.Ipc
{
    /// <summary>Portable JSON helpers for IPC payloads (no polymorphic type names).</summary>
    public static class PipeJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return string.IsNullOrEmpty(json) ? default(T) : JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
