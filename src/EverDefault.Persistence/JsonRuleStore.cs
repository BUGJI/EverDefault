using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;
using EverDefault.Core.Storage;
using EverDefault.Persistence.Json;

namespace EverDefault.Persistence
{
    public sealed class JsonRuleStore : IRuleStore
    {
        private readonly object _sync = new object();
        private readonly string _path;
        private List<RuleBase> _cache;

        public JsonRuleStore(string path)
        {
            _path = path;
        }

        public JsonRuleStore()
            : this(DataPaths.RulesFile)
        {
        }

        public IReadOnlyList<RuleBase> GetAll()
        {
            lock (_sync)
            {
                return Load().OrderBy(r => r.Priority).ThenBy(r => r.Name).ToList();
            }
        }

        public RuleBase Get(Guid id)
        {
            lock (_sync)
            {
                return Load().FirstOrDefault(r => r.Id == id);
            }
        }

        public void Upsert(RuleBase rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            lock (_sync)
            {
                var list = Load();
                var index = list.FindIndex(r => r.Id == rule.Id);
                rule.UpdatedUtc = DateTime.UtcNow;
                if (index >= 0)
                    list[index] = rule;
                else
                    list.Add(rule);

                Save(list);
            }
        }

        public void UpsertMany(IEnumerable<RuleBase> rules)
        {
            if (rules == null)
                return;

            var incoming = rules.Where(r => r != null).ToList();
            if (incoming.Count == 0)
                return;

            lock (_sync)
            {
                var list = Load();
                var index = new Dictionary<Guid, int>(list.Count);
                for (var i = 0; i < list.Count; i++)
                    index[list[i].Id] = i;

                var now = DateTime.UtcNow;
                foreach (var rule in incoming)
                {
                    rule.UpdatedUtc = now;
                    int position;
                    if (index.TryGetValue(rule.Id, out position))
                    {
                        list[position] = rule;
                    }
                    else
                    {
                        index[rule.Id] = list.Count;
                        list.Add(rule);
                    }
                }

                Save(list);
            }
        }

        public void Delete(Guid id)
        {
            DeleteMany(new[] { id });
        }

        public void DeleteMany(IEnumerable<Guid> ids)
        {
            if (ids == null)
                return;

            var wanted = new HashSet<Guid>(ids);
            if (wanted.Count == 0)
                return;

            lock (_sync)
            {
                var list = Load();
                if (list.RemoveAll(r => wanted.Contains(r.Id)) > 0)
                    Save(list);
            }
        }

        public void ReplaceAll(IEnumerable<RuleBase> rules)
        {
            lock (_sync)
            {
                var list = rules == null ? new List<RuleBase>() : rules.ToList();
                Save(list);
            }
        }

        private List<RuleBase> Load()
        {
            if (_cache == null)
                _cache = JsonFile.Read<List<RuleBase>>(_path) ?? new List<RuleBase>();
            return _cache;
        }

        private void Save(List<RuleBase> list)
        {
            JsonFile.Write(_path, list);
            _cache = list;
        }
    }
}
