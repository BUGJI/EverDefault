namespace EverDefault.Core.Model
{
    public enum RuleModule
    {
        DefaultApp = 1,
        NameSpace = 2,
        CustomRegistry = 3
    }

    public enum RuleMode
    {
        Monitor = 0,
        Schedule = 1,
        Manual = 2
    }

    public enum RuleAction
    {
        LogOnly = 0,
        Restore = 1,
        Delete = 2
    }

    public enum ChangeOrigin
    {
        Watcher = 0,
        Scheduler = 1,
        Manual = 2
    }

    public enum NameSpaceMatchType
    {
        Guid = 0,
        DisplayName = 1,
        TargetPath = 2,
        Regex = 3
    }

    public enum NameSpaceDeleteScope
    {
        NameSpaceOnly = 0,
        WithClsid = 1,
        WithClsidAndShellFolder = 2
    }

    public enum ValueMatchType
    {
        AnyChange = 0,
        Exact = 1,
        Contains = 2,
        Regex = 3
    }

    public enum OsFamily
    {
        Unknown = 0,
        Win7 = 1,
        Win8 = 2,
        Win10 = 3,
        Win11 = 4
    }

    /// <summary>Which per-user hives a rule should act on.</summary>
    public enum UserScopeMode
    {
        CurrentUser = 0,
        AllUsers = 1
    }
}
