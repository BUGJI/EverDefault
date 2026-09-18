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
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _host.Start();
        }

        protected override void OnStop()
        {
            _host.Dispose();
        }
    }
}
