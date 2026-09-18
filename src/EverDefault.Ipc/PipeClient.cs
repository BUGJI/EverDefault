using System;
using System.IO.Pipes;

namespace EverDefault.Ipc
{
    /// <summary>Synchronous request/response client for the service pipe.</summary>
    public sealed class PipeClient
    {
        private readonly string _pipeName;

        public PipeClient(string pipeName)
        {
            _pipeName = pipeName;
        }

        public PipeClient()
            : this(IpcProtocol.PipeName)
        {
        }

        public IpcResponse Send(IpcRequest request, int timeoutMs = 5000)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(timeoutMs);
                    PipeFraming.Write(pipe, PipeFraming.Serialize(request));

                    var json = PipeFraming.Read(pipe);
                    if (json == null)
                        return IpcResponse.Fail("no response from service");

                    return PipeFraming.Deserialize<IpcResponse>(json);
                }
            }
            catch (TimeoutException)
            {
                return IpcResponse.Fail("service not reachable (timeout)");
            }
            catch (Exception ex)
            {
                return IpcResponse.Fail(ex.Message);
            }
        }

        public IpcResponse Send(string command, object payload = null)
        {
            return Send(new IpcRequest
            {
                Command = command,
                Payload = payload == null ? null : PipeFraming.Serialize(payload)
            });
        }

        public T Send<T>(string command, object payload = null)
        {
            var response = Send(command, payload);
            if (!response.Success)
                throw new InvalidOperationException(response.Error);

            return string.IsNullOrEmpty(response.Payload)
                ? default(T)
                : PipeFraming.Deserialize<T>(response.Payload);
        }

        public bool IsServiceRunning()
        {
            var response = Send(IpcProtocol.CommandPing);
            return response.Success;
        }
    }
}
