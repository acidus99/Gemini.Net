using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Gemini.Net
{
    public static class ProtocolParser
    {

        //the request line is at most (1024 + 2) characters long. (max sized URL + CRLF)
        const int MaxRequestSize = 1024 + 2;

        /// <summary>
        /// Reads the request line from a client request.
        /// This looks complex, but allows for slow clients where the entire URL is not
        /// available in a single read from the buffer
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static string ReadRequestLine(Stream stream)
        {
            var requestBuffer = new List<byte>(MaxRequestSize);
            byte[] readBuffer = { 0 };

            int readCount = 0;
            while (stream.Read(readBuffer, 0, 1) == 1)
            {
                if (readBuffer[0] == (byte)'\r')
                {
                    //spec requires a \n next
                    stream.Read(readBuffer, 0, 1);
                    if (readBuffer[0] != (byte)'\n')
                    {
                        throw new ApplicationException("Invalid Request. Request line missing LF after CR");
                    }
                    break;
                }
                //keep going if we haven't read too many
                readCount++;
                if (readCount > MaxRequestSize)
                {
                    throw new ApplicationException($"Invalid Request. Did not find CRLF within {MaxRequestSize} bytes of request line");
                }
                requestBuffer.Add(readBuffer[0]);
            }
            //the URL itself should not be longer than the max size minus the trailing CRLF
            if (requestBuffer.Count > MaxRequestSize - 2)
            {
                throw new ApplicationException($"Invalid Request. URL exceeds {MaxRequestSize - 2}");
            }
            return ConvertRequestLineBytes(requestBuffer.ToArray());
        }

        //converts the bytes of the request line to a string
        public static string ConvertRequestLineBytes(byte[] requestLine)
            => //spec requires request use UTF-8
            Encoding.UTF8.GetString(requestLine);

        //converts the bytes of the response line to a string
        public static string ConvertResponseLineBytes(byte[] respLine)
            => //spec requires request use UTF-8
            Encoding.UTF8.GetString(respLine);


        /// <summary>
        /// Makes the 
        /// </summary>
        /// <param name="gurl"></param>
        /// <returns></returns>
        public static byte[] MakeRequestLine(GeminiUrl gurl)
        {
            //some server implemations are failing if you send a port that is the default
            //yes, they should fix that, but its impacting the crawlers ability to work
         if (gurl.Port == 1965)
            {
                return MakeRequestLine($"gemini://{gurl.Hostname}{gurl.Path}{gurl._url.Query}\r\n");
            }
            return MakeRequestLine($"gemini://{gurl.Hostname}:{gurl.Port}{gurl.Path}{gurl._url.Query}\r\n");
        }

        /// <summary>
        /// Makes the 
        /// </summary>
        /// <param name="gurl"></param>
        /// <returns></returns>
        public static byte[] MakeRequestLine(string gurl)
            => Encoding.UTF8.GetBytes($"{gurl}\r\n");


        public static bool ProcessServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            //TODO: TOFU logic and logic to store certificate that was received...
            return true;
        }

    }
}
