using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EverDefault.Serialization
{
    /// <summary>
    /// TypeNameHandling.Objects adds a "$type" to dictionary values (e.g. a rule's
    /// ProgIdMap) whose assembly-qualified name the whitelist binder rejects. Collections
    /// are safe to deserialize without type metadata, so suppress it for dictionaries.
    /// </summary>
    public sealed class DictionaryTypeNameResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            var type = property.PropertyType;
            if (type != null && typeof(IDictionary).IsAssignableFrom(type))
                property.TypeNameHandling = TypeNameHandling.None;

            return property;
        }
    }
}
