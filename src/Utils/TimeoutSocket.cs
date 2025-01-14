using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Gemini.Net.Utils
{
    /// <summary>
    /// Utility class to try and do a TCP connect to a host and port, but to timeout
    /// during the connect if too much time passes
    /// 
    /// </summary>
   internal class TimeoutSocket
    {
        public async Task<TcpClient> ConnectAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                return tcpClient;
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        public async Task<TcpClient> ConnectAsync(IPAddress address, int port, int timeoutMs, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeoutMs);

            var tcpClient = new TcpClient();

            try
            {
                await tcpClient.ConnectAsync(address, port, cts.Token).ConfigureAwait(false);
                return tcpClient;
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }
    }
}
