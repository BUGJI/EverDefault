using System;

namespace EverDefault.Core.Model
{
    /// <summary>
    /// A single observed registry value state. Because RegNotifyChangeKeyValue does not
    /// say what changed, watchers re-read a subtree and emit one of these per value.
    /// </summary>
    public sealed class RegistryChange
    {
        public string KeyPath { get; set; }

        public string ValueName { get; set; }

        public bool Exists { get; set; }

        public string ValueKind { get; set; }

        public string Data { get; set; }

        public long KeyLastWriteTimeUtc { get; set; }

        public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;

        public ChangeOrigin Origin { get; set; }

        public override string ToString()
        {
            return string.Format("{0}\\{1} = {2}", KeyPath, ValueName, Exists ? Data : "<none>");
        }
    }
}
