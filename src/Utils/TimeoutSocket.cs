using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Gemini.Net.Utils
{
    /// <summary>
    /// Utility class to try and do a TCP connect to a host and port, but to timeout
    /// during the connect if too much time passes
    /// 
    /// </summary>
    internal class TimeoutSocket
    {
        private bool _isConnectionSuccessful = false;
        private Exception? _socketexception = null;
        private ManualResetEvent _timeoutObject = new ManualResetEvent(false);

        public TcpClient Connect(string host, int port, int timeoutMSec)
        {
            _timeoutObject.Reset();
            _socketexception = null;

            var tcpclient = new TcpClient();

            tcpclient.BeginConnect(host, port,
                new AsyncCallback(CallBackMethod), tcpclient);

            if (_timeoutObject.WaitOne(timeoutMSec, false))
            {
                if (_isConnectionSuccessful)
                {
                    return tcpclient;
                }
                else
                {
                    throw _socketexception!;
                }
            }
            else
            {
                tcpclient.Close();
                throw new TimeoutException("TimeOut Exception");
            }
        }

        public TcpClient Connect(IPAddress iPAddress, int port, int timeoutMSec)
        {
            _timeoutObject.Reset();
            _socketexception = null;

            TcpClient tcpclient = new TcpClient();

            tcpclient.BeginConnect(iPAddress, port,
                new AsyncCallback(CallBackMethod), tcpclient);

            if (_timeoutObject.WaitOne(timeoutMSec, false))
            {
                if (_isConnectionSuccessful)
                {
                    return tcpclient;
                }
                else
                {
                    throw _socketexception!;
                }
            }
            else
            {
                tcpclient.Close();
                throw new TimeoutException("TimeOut Exception");
            }
        }

        private void CallBackMethod(IAsyncResult asyncresult)
        {
            try
            {
                _isConnectionSuccessful = false;
                TcpClient tcpclient = (TcpClient) asyncresult.AsyncState!;

                if (tcpclient.Client != null)
                {
                    tcpclient.EndConnect(asyncresult);
                    _isConnectionSuccessful = true;
                }
            }
            catch (Exception ex)
            {
                _isConnectionSuccessful = false;
                _socketexception = ex;
            }
            finally
            {
                _timeoutObject.Set();
            }
        }
    }
}
