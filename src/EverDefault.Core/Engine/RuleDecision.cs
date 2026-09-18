using CoreRule = EverDefault.Core.Model.RuleBase;

namespace EverDefault.Core.Engine
{
    public enum DecisionKind
    {
        None = 0,
        Restore = 1,
        Delete = 2,
        LogOnly = 3
    }

    public sealed class RuleDecision
    {
        public CoreRule Rule { get; set; }

        public DecisionKind Kind { get; set; }

        public string Reason { get; set; }

        public static RuleDecision None(string reason)
        {
            return new RuleDecision { Kind = DecisionKind.None, Reason = reason };
        }

        public static RuleDecision Create(CoreRule rule, DecisionKind kind, string reason)
        {
            return new RuleDecision { Rule = rule, Kind = kind, Reason = reason };
        }

        public override string ToString()
        {
            return string.Format("{0} -> {1} ({2})", Rule != null ? Rule.Name : "<none>", Kind, Reason);
        }
    }

    public sealed class ConflictReport
    {
        public CoreRule First { get; set; }

        public CoreRule Second { get; set; }

        public string Description { get; set; }

        public override string ToString()
        {
            return string.Format("{0} <-> {1}: {2}", First != null ? First.Name : "?",
                Second != null ? Second.Name : "?", Description);
        }
    }
}
