using System;
using System.Collections.Generic;

namespace EverDefault.Core.Model
{
    public abstract class RuleBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; }

        public bool Enabled { get; set; } = true;

        public RuleMode Mode { get; set; } = RuleMode.Monitor;

        /// <summary>Polling interval for <see cref="RuleMode.Schedule"/>.</summary>
        public int IntervalSeconds { get; set; } = 60;

        public RuleAction Action { get; set; } = RuleAction.Restore;

        public int Priority { get; set; }

        public OsFamily MinOs { get; set; } = OsFamily.Win7;

        public OsFamily MaxOs { get; set; } = OsFamily.Win11;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public abstract RuleModule Module { get; }

        public bool Supports(OsFamily family)
        {
            if (family == OsFamily.Unknown)
                return true;

            return family >= MinOs && family <= MaxOs;
        }
    }

    public sealed class DefaultAppRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.DefaultApp; }
        }

        public List<string> Extensions { get; set; } = new List<string>();

        public string ProgId { get; set; }

        public bool ManageOpenWith { get; set; } = true;

        public bool ManageFileAssociation { get; set; } = true;
    }

    public sealed class NameSpaceRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.NameSpace; }
        }

        public List<string> PathPatterns { get; set; } = new List<string>();

        public NameSpaceMatchType MatchType { get; set; } = NameSpaceMatchType.Guid;

        public string MatchPattern { get; set; }

        public NameSpaceDeleteScope DeleteScope { get; set; } = NameSpaceDeleteScope.NameSpaceOnly;

        public bool RefreshShell { get; set; }
    }

    public sealed class CustomRegistryRule : RuleBase
    {
        public override RuleModule Module
        {
            get { return RuleModule.CustomRegistry; }
        }

        public List<string> KeyPatterns { get; set; } = new List<string>();

        /// <summary>Empty string targets the key's default value.</summary>
        public string ValueName { get; set; } = string.Empty;

        public ValueMatchType MatchType { get; set; } = ValueMatchType.AnyChange;

        public string ExpectedKind { get; set; }

        public string ExpectedData { get; set; }

        public RuleAction OnMismatch { get; set; } = RuleAction.Restore;
    }
}
