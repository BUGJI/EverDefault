using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EverDefault.Ipc
{
    internal static class PipeSecurityFactory
    {
        public static PipeSecurity Create()
        {
            var security = new PipeSecurity();

            var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            security.AddAccessRule(new PipeAccessRule(
                authenticatedUsers,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.AddAccessRule(new PipeAccessRule(
                administrators, PipeAccessRights.FullControl, AccessControlType.Allow));

            var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            security.AddAccessRule(new PipeAccessRule(
                localSystem, PipeAccessRights.FullControl, AccessControlType.Allow));

            return security;
        }

        public static NamedPipeServerStream CreateServer(string pipeName)
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                Create());
        }
    }
}
