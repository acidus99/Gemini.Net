using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Gemini.Net.Utils;

//Using aspects of Gemini C# library SmolNetSharp for inspiration specifically:
//https://github.com/LukeEmmet/SmolNetSharp/blob/master/SmolNetSharp/Gemini.cs
// - Reading in the response line and parsing it before reading in the body
// - using a timeout and max response size to abort out early
// things I added:
// - Deciding to download the body or not based on the MIME type. This allows crawlers
// that are only interested in text content to move on more quickly and use less server
// resources
namespace Gemini.Net
{
    public class GeminiRequestor
    {
        const int ResponseLineMaxLen = 1100;

        Stopwatch ConnectTimer = new Stopwatch();
        Stopwatch DownloadTimer = new Stopwatch();
        Stopwatch AbortTimer = new Stopwatch();

        /// <summary>
        /// Amount of time, in ms, to wait before aborting the request or download
        /// </summary>
        public int AbortTimeout { get; set; } = 30 * 1000;

        /// <summary>
        /// Amount of time, in ms, to wait before aborting the request or download
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30 * 1000;

        /// <summary>
        /// Maximum amount of data to download for the response body, before aborting
        /// </summary>
        public int MaxResponseSize { get; set; } = 5 * 1024 * 1024;

        public TlsCipherSuite? NegotiatedCipherSuite { get; set; }
        public SslProtocols? NegotiatedTlsProtocol { get; set; }
        public X509Certificate2? RemoteCertificate { get; set; }

        public GeminiResponse Request(string url)
            => Request(new GeminiUrl(url));

        public GeminiResponse Request(GeminiUrl url)
            => doRequest(url, null);

        /// <summary>
        /// Make a request to a specific IP Address
        /// </summary>
        /// <param name="url"></param>
        /// <param name="iPAddress"></param>
        /// <returns></returns>
        public GeminiResponse Request(GeminiUrl url, IPAddress iPAddress)
            => doRequest(url, iPAddress);

        private GeminiResponse doRequest(GeminiUrl url, IPAddress? iPAddress)
        {
            if (!url.Url.IsAbsoluteUri)
            {
                throw new ApplicationException("Trying to request a non-absolute URL!");
            }

            IPAddress? remoteAddress = null;
            DateTime? requestSent = null;

            AbortTimer.Reset();
            ConnectTimer.Reset();
            DownloadTimer.Reset();

            try
            {
                var sock = new TimeoutSocket();
                AbortTimer.Start();

                requestSent = DateTime.Now;
                ConnectTimer.Start();

                using (TcpClient client = (iPAddress != null) ?
                    sock.Connect(iPAddress, url.Port, ConnectionTimeout) :
                    sock.Connect(url.Hostname, url.Port, ConnectionTimeout))
                {

                    remoteAddress = GetRemoteAddress(client);

                    using (SslStream sslStream = new SslStream(client.GetStream(), false,
                        new RemoteCertificateValidationCallback(ProcessServerCertificate), null))
                    {

                        sslStream.ReadTimeout = AbortTimeout;
                        sslStream.AuthenticateAsClient(url.Hostname);
                        ConnectTimer.Stop();

                        NegotiatedCipherSuite = sslStream.NegotiatedCipherSuite;
                        NegotiatedTlsProtocol = sslStream.SslProtocol;
                        RemoteCertificate = GetRemoteCertificate(sslStream);

                        sslStream.Write(GeminiParser.CreateRequestBytes(url));
                        DownloadTimer.Start();

                        string respLine = ReadResponseLine(sslStream);
                        respLine = NormalizeLegacyResponseLine(respLine);

                        var response = new GeminiResponse(url, respLine)
                        {
                            ConnectTime = Convert.ToInt32(ConnectTimer.ElapsedMilliseconds),
                            RequestSent = requestSent,
                            ResponseReceived = DateTime.Now,
                            RemoteAddress = remoteAddress
                        };

                        if (response.IsSuccess)
                        {
                            //there is only a body to download if this was a success
                            var bodyInfo = ReadBody(sslStream);
                            DownloadTimer.Stop();

                            response.IsBodyTruncated = bodyInfo.isTruncated;
                            response.BodyBytes = bodyInfo.data;
                            response.DownloadTime = Convert.ToInt32(DownloadTimer.ElapsedMilliseconds);
                        }
                        else
                        {
                            DownloadTimer.Stop();
                            response.DownloadTime = Convert.ToInt32(DownloadTimer.ElapsedMilliseconds);
                        }
                        return response;
                    }
                }
            }
            catch(Exception ex)
            {
                return new GeminiResponse(url)
                {
                    Meta = ex.Message.Trim(),
                    RemoteAddress = remoteAddress,
                    RequestSent = requestSent,
                    ResponseReceived = DateTime.Now
                };
            }
        }

        private X509Certificate2? GetRemoteCertificate(SslStream sslStream)
        {
            if(sslStream.RemoteCertificate == null)
            {
                return null;
            }

            if(sslStream.RemoteCertificate is X509Certificate2)
            {
                return (X509Certificate2)sslStream.RemoteCertificate;
            }
            return new X509Certificate2(sslStream.RemoteCertificate);
        }

        private IPAddress? GetRemoteAddress(TcpClient client)
        {
            if(client.Client.RemoteEndPoint is IPEndPoint endpoint)
            {
                return endpoint.Address;
            }
            return null;
        }

        private bool ProcessServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            //TODO: TOFU logic and logic to store certificate that was received...
            return true;
        }

        private string ReadResponseLine(Stream stream)
        {
            var respLineBuffer = new List<byte>(ResponseLineMaxLen);
            byte[] readBuffer = { 0 };

            bool hasValidLineEnding = false;

            int readCount = 0;
            //the response line is at most (2 + 1 + 1024 + 2) characters long. (a redirect with the max sized URL)
            //read that much
            while (stream.Read(readBuffer, 0, 1) == 1)
            {
                if(readBuffer[0] == (byte)'\r')
                {
                    //spec requires a \n next
                    stream.Read(readBuffer, 0, 1);
                    if (readBuffer[0] != (byte)'\n')
                    {
                        throw new Exception("Malformed Gemini header - missing LF after CR");
                    }
                    hasValidLineEnding = true;
                    break;
                }
                //keep going if we haven't read too many
                readCount++;
                if (readCount > ResponseLineMaxLen)
                {
                    throw new ApplicationException($"Invalid Gemini response line. Did not find \\r\\n within {ResponseLineMaxLen} bytes");
                }
                respLineBuffer.Add(readBuffer[0]);
                CheckAbortTimeout();
            }

            if(!hasValidLineEnding)
            {
                throw new ApplicationException($"Invalid Gemini response line. Did not find \\r\\n before connection closed");
            }

            //spec requires that the response line use UTF-8
            return Encoding.UTF8.GetString(respLineBuffer.ToArray());
        }

        /// <summary>
        /// Early Gemini systems that used a tab between the status and the META. Clean that
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private static string NormalizeLegacyResponseLine(string line)
            => line.Replace('\t', ' ');

        /// <summary>
        /// Reads the response body. Aborts if timeout or max size is exceeded
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>response body bytes</returns>
        private (byte[] data, bool isTruncated) ReadBody(Stream stream)
        {
            var respBytes = new List<byte>(10 * 1024);
            var readBuffer = new byte[4096];
            bool isTruncated = false;

            int readCount = 0;
            do
            {
                readCount = stream.Read(readBuffer, 0, readBuffer.Length);
                if (readCount > 0)
                {
                    respBytes.AddRange(readBuffer.Take(readCount));
                }
                if (respBytes.Count > MaxResponseSize)
                {
                    isTruncated = true;
                    break;
                }
                CheckAbortTimeout();
            }
            while (readCount > 0) ;
            return (respBytes.ToArray(), isTruncated);
        }

        private void CheckAbortTimeout()
        {
            if(AbortTimer.Elapsed.TotalMilliseconds > AbortTimeout)
            {
                throw new ApplicationException("Requestor abort timeout exceeded.");
            }
        }
    }
}
