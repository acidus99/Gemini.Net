using System;
using System.Net;
using System.Text;
using HashDepot;

namespace Gemini.Net;

public class GeminiUrl : IEquatable<GeminiUrl>, IComparable<GeminiUrl>
{
    public readonly Uri Url;

    public GeminiUrl(string url)
        : this(new Uri(url)) { }

    public GeminiUrl(Uri url)
    {
        Url = url;
        if (!Url.IsAbsoluteUri)
        {
            throw new ApplicationException("URL was not absolute!");
        }
        if (Url.Scheme != "gemini")
        {
            throw new ApplicationException("Attempting to create a non-Gemini URL!");
        }
        //.NET 5 is parsing URLs like gemini:/foo/bar as vaid absolute URLs, and not setting a hostname, so odd
        if (String.IsNullOrEmpty(Url.Host))
        {
            throw new ApplicationException("Invalid absolute URL. No hostname could be parsed!");
        }

        //TODO: Add URL normalization logic per RFC 3986
        //TODO: add URL restrictions in Gemini spec (no userinfo, etc)
    }

    private long? urlID;

    /// <summary>
    /// Get DocID from a URL. This happens by normalizing the URL and hashing it
    /// </summary>
    public long ID
    {
        get
        {
            if (!urlID.HasValue)
            {
                var hash = XXHash.Hash64(Encoding.UTF8.GetBytes(NormalizedUrl));
                urlID = unchecked((long)hash);
            }
            return urlID.Value;
        }
    }

    public int Port => (Url.Port > 0) ? Url.Port : 1965;

    public string Authority => $"{Hostname}:{Port}";

    //TODO: handle punycode/IDN
    //if you get a raw IPv6 address, DnsSafeHost removes the surround [] which you need
    public string Hostname
        => (Url.HostNameType == UriHostNameType.IPv6) ? Url.Host : Url.DnsSafeHost;

    public string Path => Url.AbsolutePath;

    public string Filename => System.IO.Path.GetFileName(Path);

    public string FileExtension
    {
        get
        {
            var ext = System.IO.Path.GetExtension(Path);
            return (ext.Length > 1) ? ext.Substring(1) : ext;
        }
    }

    public string Protocol
        => Url.Scheme;

    /// <summary>
    /// Does the URL have a query string (not just a ?, but a ? following by data)
    /// </summary>
    public bool HasQuery
        => (Url.Query.Length > 1);

    /// <summary>
    /// The raw, probably URL Encoded query string, without the leading ?
    /// </summary>
    public string RawQuery
        => (Url.Query.Length > 1) ? Url.Query.Substring(1) : "";

    //Just the root url for this host
    public string RootUrl
        //Some gemini servers return an error if you include the port when it is
        //running on the default. Yes, these servers should fix that, but I don't
        // want errors...
        => Port == 1965 ?
            $"gemini://{Hostname}/" :
            $"gemini://{Hostname}:{Port}/";

    /// <summary>
    /// The URL-decoded query string, without the leading ?
    /// </summary>
    public string Query
        => WebUtility.UrlDecode(RawQuery);

    // <summary>
    /// The raw, probably URL Encoded fragment, without the leading #
    /// </summary>
    public string Fragment
        => (Url.Fragment.Length > 1) ? Url.Fragment.Substring(1) : "";

    private string? normalizedUrl;

    public string NormalizedUrl
    {
        get
        {
            if (normalizedUrl == null)
            {
                //Some gemini servers return an error if you include the port when it is
                //running on the default. Yes, these servers should fix that, but I don't
                // want errors...
                normalizedUrl = (Port == 1965) ?
                    $"gemini://{Hostname}{Path}{Url.Query}" :
                    $"gemini://{Hostname}:{Port}{Path}{Url.Query}";
            }
            return normalizedUrl;
        }
    }

    public override string ToString()
        => NormalizedUrl;

    //Handles resolving relative URLs
    public static GeminiUrl? MakeUrl(GeminiUrl request, string foundUrl)
    {
        try
        {
            var newUrl = new Uri(request.Url, foundUrl);
            return (newUrl.Scheme == "gemini") ? new GeminiUrl(newUrl) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    //Handles normal urls
    public static GeminiUrl? MakeUrl(string? url)
    {
        if (url == null)
        {
            return null;
        }

        try
        {
            var newUrl = new Uri(url);
            if (newUrl.IsAbsoluteUri && newUrl.Scheme == "gemini")
            {
                return new GeminiUrl(newUrl);
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    //ultimately 2 URLs are equal if their DocID is equal
    public bool Equals(GeminiUrl? other)
        => other != null && ID.Equals(other.ID);

    public override bool Equals(object? obj)
        => Equals(obj as GeminiUrl);

    public override int GetHashCode()
        => ID.GetHashCode();

    public int CompareTo(GeminiUrl? other)
    {
        return NormalizedUrl.CompareTo(other?.NormalizedUrl ?? "");
    }
}
