using System;
using System.Collections.Generic;
using System.Linq;
using EverDefault.Core.Model;

namespace EverDefault.App
{
    public sealed class NotificationMessage
    {
        public string Title { get; set; }

        public string Text { get; set; }

        public Guid? RuleId { get; set; }

        public bool IsFailure { get; set; }
    }

    /// <summary>
    /// Turns new activity entries into at most one balloon every few seconds, so a burst of
    /// registry writes does not flood the tray.
    /// </summary>
    public sealed class NotificationCenter
    {
        private static readonly TimeSpan GlobalGap = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan RuleGap = TimeSpan.FromSeconds(60);

        private readonly Dictionary<Guid, DateTime> _lastByRule = new Dictionary<Guid, DateTime>();
        private long _lastSeenId = -1;
        private DateTime _lastShown = DateTime.MinValue;

        public event Action<NotificationMessage> Notify;

        public void Process(IEnumerable<ActivityItem> items, AppSettings settings)
        {
            var list = (items ?? Enumerable.Empty<ActivityItem>())
                .OrderBy(i => i.Id)
                .ToList();

            if (list.Count == 0)
                return;

            var max = list[list.Count - 1].Id;

            // The log may have been trimmed or cleared; restart from the current tail.
            if (_lastSeenId >= 0 && max < _lastSeenId)
                _lastSeenId = -1;

            // The first snapshot is history: remember where we are and stay silent.
            if (_lastSeenId < 0)
            {
                _lastSeenId = max;
                return;
            }

            var newItems = list.Where(i => i.Id > _lastSeenId).ToList();
            _lastSeenId = max;

            if (settings == null || !settings.ShowNotifications)
                return;

            var now = DateTime.Now;
            foreach (var item in newItems)
            {
                if (!item.IsBlocked && !item.IsFailed)
                    continue;

                if (item.IsFailed && !settings.NotifyOnFailure)
                    continue;

                if (now - _lastShown < GlobalGap)
                    continue;

                if (item.RuleId.HasValue)
                {
                    DateTime last;
                    if (_lastByRule.TryGetValue(item.RuleId.Value, out last)
                        && now - last < RuleGap)
                        continue;
                }

                _lastShown = now;
                if (item.RuleId.HasValue)
                    _lastByRule[item.RuleId.Value] = now;

                var handler = Notify;
                if (handler == null)
                    continue;

                handler(new NotificationMessage
                {
                    Title = item.IsFailed ? "有一次修改没能改回" : "已改回你的设置",
                    Text = item.Detail,
                    RuleId = item.RuleId,
                    IsFailure = item.IsFailed
                });
            }
        }
    }
}
