using System;

namespace EverDefault.Core.Model
{
    public sealed class ChangeLogEntry
    {
        public long Id { get; set; }

        public DateTime Utc { get; set; } = DateTime.UtcNow;

        public Guid? RuleId { get; set; }

        public RuleModule? Module { get; set; }

        public string KeyPath { get; set; }

        public string ValueName { get; set; }

        public string OldData { get; set; }

        public string NewData { get; set; }

        public string Action { get; set; }

        public string Result { get; set; }

        public ChangeOrigin Origin { get; set; }

        public string Message { get; set; }
    }
}
