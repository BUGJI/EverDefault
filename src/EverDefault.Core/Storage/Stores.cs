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

        void UpsertMany(IEnumerable<RuleBase> rules);

        void Delete(Guid id);

        void DeleteMany(IEnumerable<Guid> ids);

        /// <summary>Replaces the entire rule set (used by one-time migrations).</summary>
        void ReplaceAll(IEnumerable<RuleBase> rules);
    }

    public interface IBaselineStore
    {
        IReadOnlyList<BaselineRecord> GetForRule(Guid ruleId);

        void ReplaceForRule(Guid ruleId, IEnumerable<BaselineRecord> records);

        void DeleteForRule(Guid ruleId);

        void DeleteForRules(IEnumerable<Guid> ruleIds);

        /// <summary>Defers disk writes until <see cref="EndBatch"/> so bulk saves write once.</summary>
        void BeginBatch();

        void EndBatch();
    }

    public interface IChangeLogStore
    {
        /// <summary>Raised after an entry has been stored, with its assigned Id.</summary>
        event Action<ChangeLogEntry> Appended;

        /// <summary>Id of the newest entry, or 0 when the log is empty.</summary>
        long LastId { get; }

        int Count { get; }

        void Append(ChangeLogEntry entry);

        IReadOnlyList<ChangeLogEntry> Query(int max = 500, Guid? ruleId = null);

        /// <summary>Entries with an Id greater than <paramref name="afterId"/>, oldest first.</summary>
        IReadOnlyList<ChangeLogEntry> QueryAfter(long afterId, int max);

        void Trim(int keep);

        /// <summary>Removes entries older than the cutoff and caps the total count.</summary>
        int Maintenance(DateTime cutoffUtc, int maxKeep);

        void Clear();

        /// <summary>Flushes buffered writes to disk.</summary>
        void Flush();
    }

    public interface ISettingsStore
    {
        AppSettings Load();

        void Save(AppSettings settings);
    }
}
