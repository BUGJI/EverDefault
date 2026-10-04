using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EverDefault.Serialization
{
    /// <summary>
    /// Restricts polymorphic deserialization to EverDefault's own assemblies so a
    /// tampered rules file cannot instantiate arbitrary types.
    /// </summary>
    public sealed class SafeSerializationBinder : DefaultSerializationBinder
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
