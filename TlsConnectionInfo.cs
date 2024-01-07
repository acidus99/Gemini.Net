using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Gemini.Net;

/// <summary>
/// Represents information about a specific TLS connection
/// </summary>
public class TlsConnectionInfo
{
	/// <summary>
	/// The certificate the server sent for this connection.
	/// </summary>
	public X509Certificate2? RemoteCertificate { get; set; }

    /// <summary>
    /// The SSL/TLS protocol that was negotiated for this connection.
    /// </summary>
    public SslProtocols? Protocol { get; set; }

	/// <summary>
	/// The specific SSL/TLS cipher suite that was negotiated for this connection.
	/// </summary>
	public TlsCipherSuite? CipherSuite { get; set; }
}

