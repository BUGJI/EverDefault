using EverDefault.Serialization;
using Newtonsoft.Json;

namespace EverDefault.Persistence.Json
{
    internal static class Serializer
    {
        public static readonly JsonSerializerSettings Settings = Create();

        private static JsonSerializerSettings Create()
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.DateTime,
                Formatting = Formatting.Indented,
                ContractResolver = new DictionaryTypeNameResolver(),
                SerializationBinder = new SafeSerializationBinder()
            };
        }
    }
}
