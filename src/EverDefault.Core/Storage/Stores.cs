using System;
using System.Collections.Generic;
using EverDefault.Core.Model;

namespace EverDefault.Core.Storage
{
    public interface IRuleStore
    {
        IReadOnlyList<RuleBase> GetAll();

        RuleBase Get(Guid id);

        void Upsert(RuleBase rule);

        void Delete(Guid id);
    }

    public interface IBaselineStore
    {
        IReadOnlyList<BaselineRecord> GetForRule(Guid ruleId);

        void ReplaceForRule(Guid ruleId, IEnumerable<BaselineRecord> records);

        void DeleteForRule(Guid ruleId);
    }

    public interface IChangeLogStore
    {
        void Append(ChangeLogEntry entry);

        IReadOnlyList<ChangeLogEntry> Query(int max = 500, Guid? ruleId = null);

        void Trim(int keep);
    }

    public interface ISettingsStore
    {
        AppSettings Load();

        void Save(AppSettings settings);
    }
}
