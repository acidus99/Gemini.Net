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
using System.Threading;
using System.Threading.Tasks;
using Gemini.Net.Utils;

namespace Gemini.Net;

//Using aspects of Gemini C# library SmolNetSharp for inspiration specifically:
//https://github.com/LukeEmmet/SmolNetSharp/blob/master/SmolNetSharp/Gemini.cs
// - Reading in the response line and parsing it before reading in the body
// - using a timeout and max response size to abort out early
// things I added:
// - Deciding to download the body or not based on the MIME type. This allows crawlers
// that are only interested in text content to move on more quickly and use less server
// resources
public class GeminiRequestor
{
    const int ResponseLineMaxLen = 1100;

    Stopwatch ConnectTimer = new Stopwatch();
    Stopwatch DownloadTimer = new Stopwatch();

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

    public GeminiResponse Request(string url)
        => Task.Run(() => RequestAsync(new GeminiUrl(url))).Result;

    public GeminiResponse Request(GeminiUrl url)
        => Task.Run(() => RequestAsync(url)).Result;

    public GeminiResponse Request(GeminiUrl url, IPAddress iPAddress)
        => Task.Run(() => RequestAsync(url, iPAddress)).Result;

    public async Task<GeminiResponse> RequestAsync(string url, CancellationToken cancellationToken = default)
        => await RequestAsync(new GeminiUrl(url), null, cancellationToken);

    public async Task<GeminiResponse> RequestAsync(GeminiUrl url, CancellationToken cancellationToken = default)
        => await RequestAsync(url, null, cancellationToken);

    public async Task<GeminiResponse> RequestAsync(GeminiUrl url, IPAddress? iPAddress,
        CancellationToken userCancellationToken = default)
    {
        if (!url.Url.IsAbsoluteUri)
        {
            throw new ApplicationException("Trying to request a non-absolute URL!");
        }

        //Add an overall abort timeout to our user-cancellable token
        var overallCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(userCancellationToken);
        overallCancellationToken.CancelAfter(AbortTimeout);

        IPAddress? remoteAddress = iPAddress;
        DateTime? requestSent = null;
        TlsCipherSuite? cipherSuite = null;
        SslProtocols? tlsProtocol = null;
        X509Certificate2? remoteCertificate = null;

        ConnectTimer.Reset();
        DownloadTimer.Reset();

        try
        {
            var sock = new TimeoutSocket();

            requestSent = DateTime.Now;
            ConnectTimer.Start();

            using (var client = (iPAddress != null)
                       ? await sock.ConnectAsync(iPAddress, url.Port, ConnectionTimeout, overallCancellationToken.Token)
                       : await sock.ConnectAsync(url.Hostname, url.Port, ConnectionTimeout,
                           overallCancellationToken.Token))
            {
                remoteAddress = GetRemoteAddress(client);

                using (var sslStream = new SslStream(client.GetStream(), false, ProcessServerCertificate, null))
                {
                    sslStream.ReadTimeout = AbortTimeout;


                    var sslOptions = new SslClientAuthenticationOptions
                    {
                        TargetHost = url.Hostname,
                        RemoteCertificateValidationCallback = ProcessServerCertificate
                    };

                    await sslStream.AuthenticateAsClientAsync(sslOptions, overallCancellationToken.Token);
                    ConnectTimer.Stop();

                    cipherSuite = sslStream.NegotiatedCipherSuite;
                    tlsProtocol = sslStream.SslProtocol;
                    remoteCertificate = GetRemoteCertificate(sslStream);

                    await sslStream.WriteAsync(GeminiParser.CreateRequestBytes(url), overallCancellationToken.Token);
                    DownloadTimer.Start();

                    string respLine = await ReadResponseLineAsync(sslStream, overallCancellationToken.Token);
                    respLine = NormalizeLegacyResponseLine(respLine);

                    var response = new GeminiResponse(url, respLine)
                    {
                        ConnectTime = Convert.ToInt32(ConnectTimer.ElapsedMilliseconds),
                        RequestSent = requestSent,
                        ResponseReceived = DateTime.Now,
                        RemoteAddress = remoteAddress,
                        TlsInfo = new TlsConnectionInfo
                        {
                            CipherSuite = cipherSuite,
                            Protocol = tlsProtocol,
                            RemoteCertificate = remoteCertificate
                        }
                    };

                    if (response.IsSuccess)
                    {
                        var bodyInfo = await ReadBodyAsync(sslStream, userCancellationToken);
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
        catch (OperationCanceledException)
        {
            throw new TaskCanceledException("The request was canceled.");
        }
        catch (Exception ex)
        {
            return new GeminiResponse(url)
            {
                Meta = ex.Message.Trim(),
                RemoteAddress = remoteAddress,
                RequestSent = requestSent,
                ResponseReceived = DateTime.Now,
                TlsInfo = new TlsConnectionInfo
                {
                    CipherSuite = cipherSuite,
                    Protocol = tlsProtocol,
                    RemoteCertificate = remoteCertificate
                }
            };
        }
    }

    private async Task<string> ReadResponseLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var respLineBuffer = new List<byte>(ResponseLineMaxLen);
        byte[] readBuffer = { 0 };

        bool hasValidLineEnding = false;

        int readCount = 0;
        //the response line is at most (2 + 1 + 1024 + 2) characters long. (a redirect with the max sized URL)
        //read that much

        while (await stream.ReadAsync(readBuffer.AsMemory(0, 1), cancellationToken) == 1)
        {
            if (readBuffer[0] == (byte)'\r')
            {
                //spec requires a '\n' next
                await stream.ReadExactlyAsync(readBuffer, 0, 1, cancellationToken);
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
        }

       
        if (!hasValidLineEnding)
        {
            throw new ApplicationException($"Invalid Gemini response line. Did not find \\r\\n before connection closed");
        }

        //spec requires that the response line use UTF-8
        return Encoding.UTF8.GetString(respLineBuffer.ToArray());
    }

    /// <summary>
    /// Reads the response body. Aborts if timeout or max size is exceeded
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>response body bytes</returns>
    private async Task<(byte[] data, bool isTruncated)> ReadBodyAsync(Stream stream,
        CancellationToken cancellationToken)
    {
        var respBytes = new List<byte>(10 * 1024);
        var readBuffer = new byte[4096];
        bool isTruncated = false;

        int readCount = 0;
        do
        {
            readCount = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);

            if (readCount > 0)
            {
                respBytes.AddRange(readBuffer.Take(readCount));
            }

            if (respBytes.Count > MaxResponseSize)
            {
                isTruncated = true;
            }
        } while (readCount > 0 && !isTruncated);

        return (respBytes.ToArray(), isTruncated);
    }

    private bool ProcessServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        //TODO: TOFU logic and logic to store certificate that was received...
        return true;
    }

    private X509Certificate2? GetRemoteCertificate(SslStream sslStream)
    {
        if (sslStream.RemoteCertificate == null)
        {
            return null;
        }

        if (sslStream.RemoteCertificate is X509Certificate2)
        {
            return (X509Certificate2)sslStream.RemoteCertificate;
        }

        return new X509Certificate2(sslStream.RemoteCertificate);
    }

    private IPAddress? GetRemoteAddress(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is IPEndPoint endpoint)
        {
            return endpoint.Address;
        }

        return null;
    }

    /// <summary>
    /// Early Gemini systems that used a tab between the status and the META. Clean that
    /// </summary>
    /// <param name="line"></param>
    /// <returns></returns>
    private static string NormalizeLegacyResponseLine(string line)
        => line.Replace('\t', ' ');
}