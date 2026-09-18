using System;

namespace EverDefault.Core.Model
{
    public sealed class BaselineRecord
    {
        public Guid RuleId { get; set; }

        public string KeyPath { get; set; }

        public string ValueName { get; set; }

        public bool Exists { get; set; }

        public string ValueKind { get; set; }

        public string Data { get; set; }

        public long KeyLastWriteTimeUtc { get; set; }

        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    }
}
