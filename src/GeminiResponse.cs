﻿using System;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;

namespace Gemini.Net;

public class GeminiResponse
{
    /// <summary>
    /// Ensure extended code pages are supported
    /// </summary>
    static GeminiResponse()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public IPAddress? RemoteAddress { get; set; }

    public TlsConnectionInfo? TlsInfo { get; set; }

    public GeminiUrl RequestUrl { get; set; }
    public DateTime? RequestSent { get; set; }
    public DateTime? ResponseReceived { get; set; }

    public int StatusCode { get; set; }

    public string ResponseLine
        => $"{StatusCode} {Meta}";

    public bool IsConnectionError => (StatusCode == GeminiParser.ConnectionErrorStatusCode);
    public bool IsAvailable => !IsConnectionError;

    public byte[]? BodyBytes { get; set; } = null;

    private string? _bodyHash;

    /// <summary>
    /// Hash of just the Body (if it exists).
    /// </summary>
    public string? BodyHash
    {
        get
        {
            if(!HasBody)
            {
                return null;
            }
            if(_bodyHash == null)
            {
                _bodyHash = GeminiParser.GetStrongHash(BodyBytes!);
            }
            return _bodyHash;
        }
    }

    private string? _hash;

    /// <summary>
    /// Hash of the entire response (response line + optional body)
    /// </summary>
    public string Hash
    {
        get
        {
            if(_hash == null)
            {
                _hash = GeminiParser.GetStrongHash(this);
            }
            return _hash;
        }
    }

    private string? _bodyText = null;

    public string BodyText
    {
        get
        {
            if (_bodyText == null)
            {
                _bodyText = (HasBody) ?
                    GetEncoding().GetString(BodyBytes!) :
                    "";
            }
            return _bodyText;
        }
    }

    public bool HasBody => (BodyBytes?.Length > 0);

    public bool HasBodyText => (BodyText.Length > 0);

    /// <summary>
    /// The complete MIME Type, sent by the server for 2x responses
    /// </summary>
    public string? MimeType { get; protected set; }

    public string? Charset { get; protected set; }

    public string? Language { get; protected set; }

    // A response has to have a Meta, even if its an empty string
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
    public int? ConnectTime { get; set; }

    public int? DownloadTime { get; set; }

    public bool IsInput => GeminiParser.IsInputStatus(StatusCode);
    public bool IsSuccess => GeminiParser.IsSuccessStatus(StatusCode);
    public bool IsRedirect => GeminiParser.IsRedirectStatus(StatusCode);

    public bool IsFail => IsTempFail || IsPermFail;
    public bool IsSlowDown => (StatusCode == 44);
    public bool IsTempFail => GeminiParser.IsTempFailStatus(StatusCode);
    public bool IsPermFail => GeminiParser.IsPermFailStatus(StatusCode);
    public bool IsAuth => GeminiParser.IsAuthStatus(StatusCode);

    public int BodySize => HasBody ? BodyBytes!.Length : 0;

    public bool IsBodyTruncated { get; set; } = false;

    public GeminiResponse(GeminiUrl url)
    {
        RequestUrl = url;
        StatusCode = GeminiParser.ConnectionErrorStatusCode;
        Meta = "";
    }

    public GeminiResponse(GeminiUrl url, string responseLine)
    {
        RequestUrl = url;

        int metaIndex = responseLine.IndexOf(' ');
        if (metaIndex < 1)
        {
            throw new ApplicationException($"Response Line '{responseLine}' does not match Gemini format");
        }

        ParseStatusCode(responseLine.Substring(0, metaIndex));

        Meta = (metaIndex > 0 && metaIndex + 1 != responseLine.Length) ?
            responseLine.Substring(metaIndex + 1) :
            "";

        if (IsSuccess)
        {
            ParseMimeType();
        }
    }

    public GeminiUrl? GetRedirectUrl()
    {
        if(!IsRedirect)
        {
            throw new ApplicationException("Response is not a redirect!");
        }
        return GeminiUrl.MakeUrl(RequestUrl, Meta);
    }
        
    
    private void ParseStatusCode(string status)
    {
        StatusCode = Convert.ToInt16(status);
        if (StatusCode < 10 || StatusCode > 69)
        {
            throw new ApplicationException($"Invalid Status Code '{StatusCode}'");
        }
    }

    private void ParseMimeType()
    {

        //some meta can have trailing/leading whitespace. so trim it
        var normalizedMeta = Meta.Trim();

        /*
         * The original gemini spec said 
         * > If <META> is an empty string, the MIME type MUST default to "text/gemini; charset=utf-8".
         * however that is not in the more modern, up-to-date version. Adding it here for backwards support
         * since at last one capsule is serving content that way
         */
        //only need to specify the mime, since UTF-8 is assumed to be the charset
        if (normalizedMeta.Length == 0)
        {
            MimeType = "text/gemini";
        }
        else
        {
            try
            {
                var contentType = new ContentType(normalizedMeta);
                MimeType = contentType.MediaType;
                if (MimeType.StartsWith("text/"))
                {
                    Charset = contentType.CharSet?.ToLower();
                    Language = GetLanguage(contentType);
                    //force geting the encoding to validate it
                    GetEncoding();
                }
            }
            catch (Exception)
            {
                //could be a malformed lang attribute with multiple langs. just snip any params
                int paramIndex = normalizedMeta.IndexOf(";");
                MimeType = (paramIndex > 0) ?
                    Meta.Substring(0, paramIndex) :
                    Meta;
                Charset = null;
                Language = null;
            }
        }
    }

    private string? GetLanguage(ContentType parsedType)
    {
        if(parsedType.Parameters.ContainsKey("lang"))
        {
            try
            {
                CultureInfo info = new CultureInfo(parsedType.Parameters["lang"]!);
                return info.TwoLetterISOLanguageName;
            }
            catch (CultureNotFoundException)
            {
            }
        }
        return null;
    }

    private Encoding GetEncoding()
        => Encoding.GetEncoding((Charset == null) ? "utf-8" : Charset);

    public override string ToString()
    {
        var ret = ResponseLine;
        if (HasBody)
        {
            ret += $" [{BodyBytes!.Length} body bytes]";
        }
        return ret;
    }
}
