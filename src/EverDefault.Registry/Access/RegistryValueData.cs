namespace EverDefault.Registry.Access
{
    /// <summary>A registry value reduced to a portable, serializable form.</summary>
    public sealed class RegistryValueData
    {
        public string KeyPath { get; set; }

        public string Name { get; set; }

        public bool Exists { get; set; }

        public string Kind { get; set; }

        public string Data { get; set; }

        public long KeyLastWriteTimeUtc { get; set; }

        public static RegistryValueData Missing(string keyPath, string name)
        {
            return new RegistryValueData { KeyPath = keyPath, Name = name, Exists = false };
        }

        public override string ToString()
        {
            return string.Format("{0}\\{1} ({2}) {3}", KeyPath, Name, Kind, Exists ? Data : "<none>");
        }
    }
}
