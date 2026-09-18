namespace EverDefault.Ipc
{
    /// <summary>
    /// Polymorphic (de)serialization for <see cref="Core.Model.RuleBase"/> over the wire.
    /// Uses a whitelist binder so a malicious client cannot instantiate arbitrary types.
    /// </summary>
    public static class RuleCodec
    {
        private static readonly Newtonsoft.Json.JsonSerializerSettings Settings = Create();

        public static string Serialize(Core.Model.RuleBase rule)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(rule, Settings);
        }

        public static Core.Model.RuleBase Deserialize(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<Core.Model.RuleBase>(json, Settings);
        }

        public static string SerializeList(System.Collections.Generic.IEnumerable<Core.Model.RuleBase> rules)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(rules, Settings);
        }

        public static System.Collections.Generic.List<Core.Model.RuleBase> DeserializeList(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<Core.Model.RuleBase>>(json, Settings);
        }

        /// <summary>For wrapper types (e.g. RuleListResult) that contain RuleBase members.</summary>
        public static string SerializeObject(object value)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(value, Settings);
        }

        public static T DeserializeObject<T>(string json)
        {
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json, Settings);
        }

        private static Newtonsoft.Json.JsonSerializerSettings Create()
        {
            return new Newtonsoft.Json.JsonSerializerSettings
            {
                TypeNameHandling = Newtonsoft.Json.TypeNameHandling.Objects,
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                SerializationBinder = new Binder()
            };
        }

        private sealed class Binder : Newtonsoft.Json.Serialization.DefaultSerializationBinder
        {
            public override System.Type BindToType(string assemblyName, string typeName)
            {
                var type = System.Type.GetType(typeName + ", " + assemblyName, false);
                if (type == null)
                    return null;

                var ns = type.Namespace ?? string.Empty;
                if (ns.StartsWith("EverDefault.", System.StringComparison.Ordinal))
                    return type;

                throw new Newtonsoft.Json.JsonSerializationException("Type not allowed: " + type.FullName);
            }
        }
    }
}
