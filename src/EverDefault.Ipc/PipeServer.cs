using System;
using System.IO.Pipes;
using System.Threading;

namespace EverDefault.Ipc
{
    /// <summary>
    /// Listens on a named pipe and dispatches each request to the supplied handler.
    /// One thread per connected client; clients are short-lived request/response calls.
    /// </summary>
    public sealed class PipeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly Func<IpcRequest, IpcResponse> _handler;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread _acceptThread;

        public event Action<string> Log;

        public PipeServer(string pipeName, Func<IpcRequest, IpcResponse> handler)
        {
            _pipeName = pipeName;
            _handler = handler;
        }

        public void Start()
        {
            if (_acceptThread != null)
                return;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "IpcAccept:" + _pipeName
            };
            _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (!_stop.WaitOne(0))
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = PipeSecurityFactory.CreateServer(_pipeName);
                    server.WaitForConnection();

                    var client = server;
                    server = null;
                    ThreadPool.QueueUserWorkItem(state => HandleClient((NamedPipeServerStream)state), client);
                }
                catch (Exception ex)
                {
                    if (_stop.WaitOne(0))
                        break;

                    Log?.Invoke("pipe accept: " + ex.Message);
                    if (_stop.WaitOne(250))
                        break;
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }

        private void HandleClient(NamedPipeServerStream client)
        {
            using (client)
            {
                try
                {
                    while (client.IsConnected)
                    {
                        var json = PipeFraming.Read(client);
                        if (json == null)
                            break;

                        IpcResponse response;
                        try
                        {
                            var request = PipeFraming.Deserialize<IpcRequest>(json);
                            response = _handler(request) ?? IpcResponse.Fail("no handler response");
                        }
                        catch (Exception ex)
                        {
                            response = IpcResponse.Fail(ex.Message);
                        }

                        PipeFraming.Write(client, PipeFraming.Serialize(response));
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke("pipe client: " + ex.Message);
                }
            }
        }

        public void Dispose()
        {
            _stop.Set();
        }
    }
}
