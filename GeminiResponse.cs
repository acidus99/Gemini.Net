﻿using System;
using System.Text;
using HashDepot;

namespace Gemini.Net
{
    public class GeminiResponse
    {

        public GeminiUrl RequestUrl { get; private set; }

        /// <summary>
        /// The full raw response line from the server. [status][0x20][meta][CR][LF]
        /// </summary>
        public string ResponseLine { get; private set; }

        public int StatusCode { get; private set; }

        public ConnectStatus ConnectStatus { get; set; }

        /// <summary>
        /// Did we deliberately skip the body?
        /// </summary>
        public bool BodySkipped { get; internal set; }

        public byte[] BodyBytes { get; private set; }

        public uint BodyHash
            => (HasBody) ? XXHash.Hash32(BodyBytes) : 0;

        public string BodyText { get; private set; }

        public bool HasBody => (BodyBytes?.Length > 0);

        /// <summary>
        /// The complete MIME Type, sent by the server for 2x responses
        /// </summary>
        public string MimeType { get; private set; }

        public ContentType ContentType { get; internal set; } = ContentType.Unknown;

        /// <summary>
        /// Data about the response, whose meaning is status dependent
        /// 1x = Prompt to display user for input
        /// 2x = Mimetype
        /// 3x = Redirection URL
        /// 4x, 5x, or 6x = Error Message
        /// Also gets set if we hit a connection error
        /// </summary>
        public string Meta { get; set; }

        /// <summary>
        /// Latency of the request/resp, in ms
        /// </summary>
        public int ConnectTime { get; internal set; }

        public int DownloadTime { get; internal set; }

        public bool IsImageResponse => HasBody && MimeType.StartsWith("image/");
        public bool IsTextResponse => HasBody && MimeType.StartsWith("text/");

        public bool IsInput => InStatusRange(10);
        public bool IsSuccess => InStatusRange(20);
        public bool IsRedirect => InStatusRange(30);
        public bool IsTempFail => InStatusRange(40);
        public bool IsPermFail => InStatusRange(50);
        public bool IsAuth => InStatusRange(60);

        private bool InStatusRange(int low)
            => (StatusCode >= low && StatusCode<= low + 9);

        public int BodySize => HasBody ? BodyBytes.Length : 0;

        public GeminiResponse(GeminiUrl url)
        {
            RequestUrl = url;
            ConnectStatus = ConnectStatus.Error;
            StatusCode = 0;
            MimeType = "";
            Meta = "";
            ConnectTime = 0;
            DownloadTime = 0;
            BodySkipped = false;
        }

        public GeminiResponse(GeminiUrl url, string responseLine)
        {
            RequestUrl = url;

            ConnectStatus = ConnectStatus.Success;
            ResponseLine = responseLine;

            int x = responseLine.IndexOf(' ');
            if (x < 1)
            {
                throw new ApplicationException($"Response Line '{responseLine}' does not match Gemini format");
            }

            ParseStatusCode(responseLine.Substring(0, x));
            ParseMeta((x > 0 && x + 1 != responseLine.Length) ?
                responseLine.Substring(x + 1) : "");
        }

        private void ParseStatusCode(string status)
        {
            StatusCode = Convert.ToInt16(status);
            if (StatusCode < 10 || StatusCode > 69)
            {
                throw new ApplicationException($"Invalid Static Code '{StatusCode}'");
            }
        }

        private void ParseMeta(string extraData)
        {
            Meta = extraData;
            if(IsSuccess)
            {
                /*
                 * The original gemini spec said 
                 * > If <META> is an empty string, the MIME type MUST default to "text/gemini; charset=utf-8".
                 * however that is not in the more modern, up-to-date version. Adding it here for backwards support
                 * since at last one capsule is serving content that way
                 */
                //only need to specify the mime, since UTF-8 is assumed to be the charset
                MimeType = (Meta.Length > 0) ? Meta : "text/gemini";

                //this is a back, we should do this off the body bytes with a magic file
                if (IsTextResponse)
                {
                    ContentType = ContentType.Text;
                }
                else if (IsImageResponse)
                {
                    ContentType = ContentType.Image;
                }
                else
                {
                    ContentType = ContentType.Binary;
                }
            }
        }

        public void ParseBody(byte[] body)
        {
            if (body.Length > 0)
            {
                BodyBytes = body;

                if (IsTextResponse)
                {
                    //TODO add charset parsing here
                    BodyText = Encoding.UTF8.GetString(BodyBytes);
                }
            }
        }

        public override string ToString()
        {
            var s = ResponseLine;
            if(IsSuccess)
            {
                if (IsTextResponse)
                {
                    s += "\n" + BodyText;
                } else
                {
                    s += $"\nBinary data ({BodyBytes.Length} bytes)";
                }
            }
            return s;
        }

    }

    /// <summary>
    /// Status of the network connection made for a Gemini request.
    /// Used to show errors at the network level vs protocol level
    /// </summary>
    public enum ConnectStatus : int
    {
        Unknown = 0,
        Success = 1,
        Error = 2,
    }

    public enum ContentType : int
    {
        Unknown = 0,
        Text = 1,
        Image = 2,
        Binary = 3,
    }
}
