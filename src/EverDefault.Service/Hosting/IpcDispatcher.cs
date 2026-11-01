using System;
using EverDefault.Core.Model;
using EverDefault.Ipc;

namespace EverDefault.Service.Hosting
{
    /// <summary>Translates pipe requests into EngineHost operations.</summary>
    public sealed class IpcDispatcher
    {
        private readonly EngineHost _host;

        public IpcDispatcher(EngineHost host)
        {
            _host = host;
        }

        public IpcResponse Handle(IpcRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Command))
                return IpcResponse.Fail("empty request");

            try
            {
                switch (request.Command)
                {
                    case IpcProtocol.CommandPing:
                        return IpcResponse.Ok("\"pong\"");

                    case IpcProtocol.CommandGetStatus:
                        return IpcResponse.Ok(PipeJson.Serialize(_host.GetStatus()));

                    case IpcProtocol.CommandGetRules:
                        return IpcResponse.Ok(RuleCodec.SerializeObject(_host.GetRules()));

                    case IpcProtocol.CommandSaveRule:
                        var ruleJson = PipeJson.Deserialize<string>(request.Payload);
                        var rule = RuleCodec.Deserialize(ruleJson);
                        if (rule == null)
                            return IpcResponse.Fail("invalid rule payload");
                        _host.SaveRule(rule);
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandSaveRules:
                        var ruleJsonList = PipeJson.Deserialize<System.Collections.Generic.List<string>>(request.Payload);
                        if (ruleJsonList == null || ruleJsonList.Count == 0)
                            return IpcResponse.Ok();

                        var rules = new System.Collections.Generic.List<RuleBase>();
                        foreach (var json in ruleJsonList)
                        {
                            var parsed = RuleCodec.Deserialize(json);
                            if (parsed == null)
                                return IpcResponse.Fail("invalid rule payload in batch");
                            rules.Add(parsed);
                        }

                        _host.SaveRules(rules);
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandDeleteRule:
                        _host.DeleteRule(Guid.Parse(PipeJson.Deserialize<string>(request.Payload)));
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandDeleteRules:
                        var idList = PipeJson.Deserialize<System.Collections.Generic.List<string>>(request.Payload);
                        if (idList == null || idList.Count == 0)
                            return IpcResponse.Ok();

                        var ids = new System.Collections.Generic.List<Guid>();
                        foreach (var raw in idList)
                            ids.Add(Guid.Parse(raw));
                        _host.DeleteRules(ids);
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandGetLog:
                        var query = string.IsNullOrEmpty(request.Payload)
                            ? new LogQuery()
                            : PipeJson.Deserialize<LogQuery>(request.Payload);
                        var entries = _host.GetLog(query.Max, query.RuleId, query.AfterId);
                        return IpcResponse.Ok(PipeJson.Serialize(new LogPage
                        {
                            Entries = new System.Collections.Generic.List<ChangeLogEntry>(entries),
                            LastId = _host.GetLogLastId()
                        }));

                    case IpcProtocol.CommandClearLog:
                        _host.ClearLog();
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandGetConflicts:
                        return IpcResponse.Ok(PipeJson.Serialize(_host.GetRules().Conflicts));

                    case IpcProtocol.CommandGetSettings:
                        return IpcResponse.Ok(PipeJson.Serialize(_host.GetSettings()));

                    case IpcProtocol.CommandSaveSettings:
                        _host.SaveSettings(PipeJson.Deserialize<AppSettings>(request.Payload));
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandSetMonitoring:
                        _host.SetMonitoring(bool.Parse(PipeJson.Deserialize<string>(request.Payload)));
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandReloadRules:
                        _host.Reload();
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandPauseAll:
                        _host.PauseAll(PipeJson.Deserialize<int>(request.Payload));
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandResumeAll:
                        _host.ResumeAll();
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandScanNamespaces:
                        return IpcResponse.Ok(PipeJson.Serialize(
                            new NameSpaceScanResult { Entries = _host.ScanNameSpaces() }));

                    case IpcProtocol.CommandRunAll:
                        return IpcResponse.Ok(PipeJson.Serialize(_host.RunAllNow()));

                    case IpcProtocol.CommandRunRule:
                        return IpcResponse.Ok(PipeJson.Serialize(
                            _host.RunRuleNow(Guid.Parse(PipeJson.Deserialize<string>(request.Payload)))));

                    case IpcProtocol.CommandRunRules:
                        var runIdList = PipeJson.Deserialize<System.Collections.Generic.List<string>>(request.Payload);
                        var runIds = new System.Collections.Generic.List<Guid>();
                        foreach (var raw in runIdList ?? new System.Collections.Generic.List<string>())
                            runIds.Add(Guid.Parse(raw));
                        return IpcResponse.Ok(PipeJson.Serialize(_host.RunRulesNow(runIds)));

                    case IpcProtocol.CommandSaveRuleAndRun:
                        var runRuleJson = PipeJson.Deserialize<string>(request.Payload);
                        var runRule = RuleCodec.Deserialize(runRuleJson);
                        if (runRule == null)
                            return IpcResponse.Fail("invalid rule payload");
                        return IpcResponse.Ok(PipeJson.Serialize(_host.SaveRuleAndRun(runRule)));

                    case IpcProtocol.CommandSaveRulesAndRun:
                        var runRuleJsonList = PipeJson.Deserialize<System.Collections.Generic.List<string>>(request.Payload);
                        if (runRuleJsonList == null || runRuleJsonList.Count == 0)
                            return IpcResponse.Ok(PipeJson.Serialize(
                                _host.SaveRulesAndRun(new System.Collections.Generic.List<RuleBase>())));

                        var savedRules = new System.Collections.Generic.List<RuleBase>();
                        foreach (var json in runRuleJsonList)
                        {
                            var parsed = RuleCodec.Deserialize(json);
                            if (parsed == null)
                                return IpcResponse.Fail("invalid rule payload in batch");
                            savedRules.Add(parsed);
                        }

                        return IpcResponse.Ok(PipeJson.Serialize(_host.SaveRulesAndRun(savedRules)));

                    default:
                        return IpcResponse.Fail("unknown command: " + request.Command);
                }
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(ex.Message);
            }
        }
    }
}
