using System;
using System.Collections.Generic;
using System.Linq;

namespace EverDefault.Core.Model
{
    /// <summary>
    /// Human-readable export/import document. Deliberately does NOT use runtime type
    /// names so the JSON stays clean and hand-editable.
    /// </summary>
    public sealed class RuleDocument
    {
        public int Version { get; set; } = 1;

        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        public List<RuleDto> Rules { get; set; } = new List<RuleDto>();
    }

    public sealed class RuleDto
    {
        public string Kind { get; set; }

        public string Name { get; set; }

        public bool Enabled { get; set; } = true;

        public string Mode { get; set; } = "Monitor";

        public string Action { get; set; } = "Restore";

        public int IntervalSeconds { get; set; } = 60;

        public int Priority { get; set; }

        public string MinOs { get; set; } = "Win7";

        public string MaxOs { get; set; } = "Win11";

        // DefaultApp
        public List<string> Extensions { get; set; }

        public string ProgId { get; set; }

        public bool? ManageOpenWith { get; set; }

        public bool? ManageFileAssociation { get; set; }

        // NameSpace
        public List<string> PathPatterns { get; set; }

        public string NameSpaceMatchType { get; set; }

        public string MatchPattern { get; set; }

        public string DeleteScope { get; set; }

        public bool? RefreshShell { get; set; }

        // CustomRegistry
        public List<string> KeyPatterns { get; set; }

        public string ValueName { get; set; }

        public string ValueMatchType { get; set; }

        public string ExpectedKind { get; set; }

        public string ExpectedData { get; set; }

        public string OnMismatch { get; set; }
    }

    public static class RuleDocumentMapper
    {
        public static RuleDocument ToDocument(IEnumerable<RuleBase> rules)
        {
            var document = new RuleDocument();
            foreach (var rule in rules ?? Enumerable.Empty<RuleBase>())
                document.Rules.Add(ToDto(rule));
            return document;
        }

        public static RuleDto ToDto(RuleBase rule)
        {
            var dto = new RuleDto
            {
                Kind = rule.Module.ToString(),
                Name = rule.Name,
                Enabled = rule.Enabled,
                Mode = rule.Mode.ToString(),
                Action = rule.Action.ToString(),
                IntervalSeconds = rule.IntervalSeconds,
                Priority = rule.Priority,
                MinOs = rule.MinOs.ToString(),
                MaxOs = rule.MaxOs.ToString()
            };

            var app = rule as DefaultAppRule;
            if (app != null)
            {
                dto.Extensions = app.Extensions;
                dto.ProgId = app.ProgId;
                dto.ManageOpenWith = app.ManageOpenWith;
                dto.ManageFileAssociation = app.ManageFileAssociation;
                return dto;
            }

            var nameSpace = rule as NameSpaceRule;
            if (nameSpace != null)
            {
                dto.PathPatterns = nameSpace.PathPatterns;
                dto.NameSpaceMatchType = nameSpace.MatchType.ToString();
                dto.MatchPattern = nameSpace.MatchPattern;
                dto.DeleteScope = nameSpace.DeleteScope.ToString();
                dto.RefreshShell = nameSpace.RefreshShell;
                return dto;
            }

            var custom = rule as CustomRegistryRule;
            if (custom != null)
            {
                dto.KeyPatterns = custom.KeyPatterns;
                dto.ValueName = custom.ValueName;
                dto.ValueMatchType = custom.MatchType.ToString();
                dto.ExpectedKind = custom.ExpectedKind;
                dto.ExpectedData = custom.ExpectedData;
                dto.OnMismatch = custom.OnMismatch.ToString();
            }

            return dto;
        }

        /// <summary>Builds a fresh rule (new Id) from a DTO. Returns null with an error on bad input.</summary>
        public static RuleBase ToRule(RuleDto dto, out string error)
        {
            error = null;
            if (dto == null)
            {
                error = "空规则项";
                return null;
            }

            RuleModule module;
            if (!Enum.TryParse(dto.Kind, true, out module))
            {
                error = "未知模块: " + dto.Kind;
                return null;
            }

            RuleBase rule;
            switch (module)
            {
                case RuleModule.DefaultApp:
                    rule = new DefaultAppRule
                    {
                        Extensions = dto.Extensions ?? new List<string>(),
                        ProgId = dto.ProgId,
                        ManageOpenWith = dto.ManageOpenWith ?? true,
                        ManageFileAssociation = dto.ManageFileAssociation ?? true
                    };
                    break;

                case RuleModule.NameSpace:
                    rule = new NameSpaceRule
                    {
                        PathPatterns = dto.PathPatterns ?? new List<string>(),
                        MatchType = Parse(dto.NameSpaceMatchType, NameSpaceMatchType.Guid),
                        MatchPattern = dto.MatchPattern,
                        DeleteScope = Parse(dto.DeleteScope, NameSpaceDeleteScope.NameSpaceOnly),
                        RefreshShell = dto.RefreshShell ?? false
                    };
                    break;

                case RuleModule.CustomRegistry:
                    rule = new CustomRegistryRule
                    {
                        KeyPatterns = dto.KeyPatterns ?? new List<string>(),
                        ValueName = dto.ValueName ?? string.Empty,
                        MatchType = Parse(dto.ValueMatchType, ValueMatchType.AnyChange),
                        ExpectedKind = dto.ExpectedKind,
                        ExpectedData = dto.ExpectedData,
                        OnMismatch = Parse(dto.OnMismatch, RuleAction.Restore)
                    };
                    break;

                default:
                    error = "不支持的模块: " + dto.Kind;
                    return null;
            }

            rule.Id = Guid.NewGuid();
            rule.Name = dto.Name;
            rule.Enabled = dto.Enabled;
            rule.Mode = Parse(dto.Mode, RuleMode.Monitor);
            rule.Action = Parse(dto.Action, RuleAction.Restore);
            rule.IntervalSeconds = dto.IntervalSeconds > 0 ? dto.IntervalSeconds : 60;
            rule.Priority = dto.Priority;
            rule.MinOs = Parse(dto.MinOs, OsFamily.Win7);
            rule.MaxOs = Parse(dto.MaxOs, OsFamily.Win11);
            rule.CreatedUtc = DateTime.UtcNow;
            rule.UpdatedUtc = DateTime.UtcNow;
            return rule;
        }

        private static TEnum Parse<TEnum>(string value, TEnum fallback) where TEnum : struct
        {
            TEnum parsed;
            return !string.IsNullOrEmpty(value) && Enum.TryParse(value, true, out parsed) ? parsed : fallback;
        }
    }
}
