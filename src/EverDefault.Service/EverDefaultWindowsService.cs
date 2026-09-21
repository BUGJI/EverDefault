using System.ServiceProcess;
using EverDefault.Service.Hosting;

namespace EverDefault.Service
{
    public sealed class EverDefaultWindowsService : ServiceBase
    {
        private readonly EngineHost _host;

        public EverDefaultWindowsService(EngineHost host)
        {
            _host = host;
            ServiceName = "EverDefault";
            CanStop = true;
            CanPauseAndContinue = false;
            CanHandleSessionChangeEvent = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _host.Start();
        }

        protected override void OnSessionChange(SessionChangeDescription changeDescription)
        {
            switch (changeDescription.Reason)
            {
                case SessionChangeReason.SessionLogon:
                case SessionChangeReason.SessionUnlock:
                case SessionChangeReason.RemoteConnect:
                    _host.OnUserSessionChanged();
                    break;
            }
        }

        protected override void OnStop()
        {
            _host.Dispose();
        }
    }
}
