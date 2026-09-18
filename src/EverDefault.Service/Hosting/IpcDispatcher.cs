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

                    case IpcProtocol.CommandDeleteRule:
                        _host.DeleteRule(Guid.Parse(PipeJson.Deserialize<string>(request.Payload)));
                        return IpcResponse.Ok();

                    case IpcProtocol.CommandGetLog:
                        var query = string.IsNullOrEmpty(request.Payload)
                            ? new LogQuery()
                            : PipeJson.Deserialize<LogQuery>(request.Payload);
                        var entries = _host.GetLog(query.Max, query.RuleId);
                        return IpcResponse.Ok(PipeJson.Serialize(new LogPage { Entries = new System.Collections.Generic.List<ChangeLogEntry>(entries) }));

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
