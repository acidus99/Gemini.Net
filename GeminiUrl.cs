using System;
using System.Net;
using HashDepot;
using System.Text;

namespace Gemini.Net
{
    public class GeminiUrl :IEquatable<GeminiUrl>, IComparable<GeminiUrl>
    {
        public Uri _url;

        public GeminiUrl(string url)
            : this(new Uri(url)) { }

        public GeminiUrl(Uri url)
        {
            _url = url;
            if(!_url.IsAbsoluteUri)
            {
                throw new ApplicationException("URL was not absolute!");
            }
            if(_url.Scheme != "gemini")
            {
                throw new ApplicationException("Attempting to create a non-Gemini URL!");
            }
            //.NET 5 is parsing URLs like gemini:/foo/bar as vaid absolute URLs, and not setting a hostname, so odd
            if(String.IsNullOrEmpty(_url.Host))
            {
                throw new ApplicationException("Invalid absolute URL. No hostname could be parsed!");
            }

            //TODO: Add URL normalization logic per RFC 3986
            //TODO: add URL restrictions in Gemini spec (no userinfo, etc)
        }

        private ulong? docID;

        /// <summary>
        /// Get DocID from a URL. This happens by normalizing the URL and hashing it
        /// </summary>
        public ulong DocID
        {
            get
            {
                if (!docID.HasValue)
                {
                    docID = XXHash.Hash64(Encoding.UTF8.GetBytes(NormalizedUrl));
                }
                return docID.Value;
            }
        }

        public int Port => (_url.Port > 0) ? _url.Port : 1965;

        public string Authority => $"{Hostname}:{Port}";

        //TODO: handle punycode/IDN
        //if you get a raw IPv6 address, DnsSafeHost removes the surround [] which you need
        public string Hostname
            => (_url.HostNameType == UriHostNameType.IPv6) ? _url.Host : _url.DnsSafeHost;

        public string Path => _url.AbsolutePath;

        public string Filename => System.IO.Path.GetFileName(Path);

        public string FileExtension
        {
            get
            {
                var ext = System.IO.Path.GetExtension(Path);
                return (ext.Length > 1) ? ext.Substring(1) : ext;
            }
        }

        /// <summary>
        /// Does the URL have a query string (not just a ?, but a ? following by data)
        /// </summary>
        public bool HasQuery
            => (_url.Query.Length > 1);

        /// <summary>
        /// The raw, probably URL Encoded query string, without the leading ?
        /// </summary>
        public string RawQuery
            => (_url.Query.Length > 1) ? _url.Query.Substring(1) : "";

        /// <summary>
        /// The URL-decoded query string, without the leading ?
        /// </summary>
        public string Query
            => WebUtility.UrlDecode(RawQuery);

        // <summary>
        /// The raw, probably URL Encoded fragment, without the leading #
        /// </summary>
        public string Fragment
            => (_url.Fragment.Length > 1) ? _url.Fragment.Substring(1) : "";

        public string NormalizedUrl
            //Some gemini servers return an error if you include the port when it is
            //running on the default. Yes, these servers should fix that, but I don't
            // want errors...
            => Port == 1965 ?
                $"gemini://{Hostname}{Path}{_url.Query}" :
                $"gemini://{Hostname}:{Port}{Path}{_url.Query}";

        public override string ToString()
            => NormalizedUrl;

        //Handles resolving relative URLs
        public static GeminiUrl MakeUrl(GeminiUrl request, string foundUrl)
        {
            Uri newUrl = null;
            try
            {
                newUrl = new Uri(request._url, foundUrl);
                return (newUrl.Scheme == "gemini") ? new GeminiUrl(newUrl) : null;
            } catch(Exception)
            {
                return null;
            }
        }

        //ultimately 2 URLs are equal if their DocID is equal
        public bool Equals(GeminiUrl other)
            => other != null && DocID.Equals(other.DocID);

        public override bool Equals(object obj)
            => Equals(obj as GeminiUrl);

        public override int GetHashCode()
            => DocID.GetHashCode();

        public int CompareTo(GeminiUrl other)
        {
            return this.NormalizedUrl.CompareTo(other.NormalizedUrl);
        }
    }
}
