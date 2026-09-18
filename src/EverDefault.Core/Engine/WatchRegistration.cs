using System.Collections.Generic;

namespace EverDefault.Core.Engine
{
    public sealed class WatchRegistration
    {
        public string KeyPath { get; set; }

        public bool Recursive { get; set; } = true;

        public override string ToString()
        {
            return KeyPath + (Recursive ? " (subtree)" : string.Empty);
        }
    }

    public sealed class RuleListResult
    {
        public List<Model.RuleBase> Rules { get; set; } = new List<Model.RuleBase>();

        public List<string> Conflicts { get; set; } = new List<string>();

        public List<System.Guid> ConflictingRuleIds { get; set; } = new List<System.Guid>();
    }
}
