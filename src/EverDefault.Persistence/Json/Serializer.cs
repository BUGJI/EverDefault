using System;
using Newtonsoft.Json;

namespace EverDefault.Persistence.Json
{
    internal static class Serializer
    {
        public static readonly JsonSerializerSettings Settings = Create();

        private static JsonSerializerSettings Create()
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.DateTime,
                Formatting = Formatting.Indented
            };

            settings.SerializationBinder = new SafeSerializationBinder();
            return settings;
        }
    }

    /// <summary>
    /// Restricts polymorphic deserialization to EverDefault's own assemblies so a
    /// tampered rules file cannot instantiate arbitrary types.
    /// </summary>
    internal sealed class SafeSerializationBinder : Newtonsoft.Json.Serialization.DefaultSerializationBinder
    {
        public override Type BindToType(string assemblyName, string typeName)
        {
            var type = Type.GetType(typeName + ", " + assemblyName, false);
            if (type == null)
                return null;

            var ns = type.Namespace ?? string.Empty;
            if (ns.StartsWith("EverDefault.", StringComparison.Ordinal))
                return type;

            throw new JsonSerializationException("Type not allowed: " + type.FullName);
        }
    }
}
