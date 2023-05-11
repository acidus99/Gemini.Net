using System;
using System.Globalization;
using System.Text;

using System.Net.Mime;
using HashDepot;

namespace Gemini.Net
{
    public class GeminiResponse
    {

        static GeminiResponse()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public GeminiUrl RequestUrl { get; set; }
        public DateTime RequestSent { get; set; } = DateTime.Now;

        public DateTime ResponseReceived { get; set; } = DateTime.Now;

        /// <summary>
        /// The full raw response line from the server. [status][0x20][meta][CR][LF]
        /// </summary>
        public string ResponseLine { get; protected set; }

        public int StatusCode { get; set; }

        public bool IsConnectionError => (StatusCode == GeminiParser.ConnectionErrorStatusCode);
        public bool IsAvailable => !IsConnectionError;

        public byte[] BodyBytes { get; protected set; }

        public uint? BodyHash
            => (HasBody) ? XXHash.Hash32(BodyBytes): null;

        private string bodyText = null;

        public string BodyText
        {
            get
            {
                if (bodyText == null)
                {
                    bodyText = (HasBody) ?
                        GetEncoding().GetString(BodyBytes) :
                        "";
                }
                return bodyText;
            }
        }

        public bool HasBody => (BodyBytes?.Length > 0);

        /// <summary>
        /// The complete MIME Type, sent by the server for 2x responses
        /// </summary>
        public string MimeType { get; protected set; }

        public string Charset { get; protected set; }

        public string Language { get; protected set; }

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
        public int ConnectTime { get; set; }

        public int DownloadTime { get; set; }

        public bool IsImageResponse => HasBody && MimeType.StartsWith("image/");
        public bool IsTextResponse => HasBody && MimeType.StartsWith("text/");

        public bool IsInput => GeminiParser.IsInputStatus(StatusCode);
        public bool IsSuccess => GeminiParser.IsSuccessStatus(StatusCode);
        public bool IsRedirect => GeminiParser.IsRedirectStatus(StatusCode);

        public bool IsFail => IsTempFail || IsTempFail;

        public bool IsTempFail => GeminiParser.IsTempFailStatus(StatusCode);
        public bool IsPermFail => GeminiParser.IsPermFailStatus(StatusCode);
        public bool IsAuth => GeminiParser.IsAuthStatus(StatusCode);

        public int BodySize => HasBody ? BodyBytes.Length : 0;

        public bool IsBodyTruncated { get; set; } = false;

        public GeminiResponse(GeminiUrl url = null)
        {
            RequestUrl = url;
            StatusCode = GeminiParser.ConnectionErrorStatusCode;
            MimeType = "";
            Meta = "";
            ConnectTime = 0;
            DownloadTime = 0;
        }

        public GeminiResponse(GeminiUrl url, string responseLine)
        {
            RequestUrl = url;
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
                if (Meta.Length == 0)
                {
                    MimeType = "text/gemini";
                }
                else
                {
                    try
                    {
                        var contentType = new ContentType(Meta);
                        MimeType = contentType.MediaType;
                        if (MimeType.StartsWith("text/"))
                        {
                            Charset = contentType.CharSet?.ToLower();
                            Language = GetLanugage(contentType);
                            //force geting the encoding to valid it
                            GetEncoding();
                        }
                    }
                    catch (Exception)
                    {
                        //could be a malformed lang attribute with multiple langs. just snip any params
                        int paramIndex = Meta.IndexOf(";");
                        MimeType = (paramIndex > 0) ?
                            Meta.Substring(0, paramIndex) :
                            Meta;
                        Charset = null;
                        Language = null;
                    }
                }
            }
        }

        private string GetLanugage(ContentType parsedType)
        {
            if(parsedType.Parameters.ContainsKey("lang"))
            {
                try
                {
                    CultureInfo info = new CultureInfo(parsedType.Parameters["lang"]);
                    return info.TwoLetterISOLanguageName;
                } catch(CultureNotFoundException)
                {
                }
            }
            return null;
        }

        public void ParseBody(byte[] body)
        {
            if (body.Length > 0)
            {
                BodyBytes = body;

              
            }
        }

        private Encoding GetEncoding()
            => Encoding.GetEncoding((Charset == null) ? "utf-8" : Charset);

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
}
