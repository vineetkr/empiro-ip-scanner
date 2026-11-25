using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Empiro.IpScanner.Wpf;

/// <summary>Uses ONVIF Device/Media SOAP services to resolve a camera's first RTSP profile URI.</summary>
internal sealed class OnvifMediaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _username;
    private readonly string _password;
    private const string DeviceNs = "http://www.onvif.org/ver10/device/wsdl";
    private const string MediaNs = "http://www.onvif.org/ver10/media/wsdl";
    private const string SchemaNs = "http://www.onvif.org/ver10/schema";

    public OnvifMediaClient(string username, string password)
    {
        _username = username;
        _password = password;
        var handler = new HttpClientHandler
        {
            Credentials = new NetworkCredential(username, password),
            PreAuthenticate = true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task<string> GetRtspUriAsync(string deviceServiceUrl, CancellationToken cancellationToken)
    {
        XNamespace tds = DeviceNs;
        XNamespace trt = MediaNs;
        XNamespace tt = SchemaNs;
        var capabilities = await PostAsync(deviceServiceUrl,
            new XElement(tds + "GetCapabilities", new XElement(tds + "Category", "Media")),
            "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", cancellationToken);
        var mediaEndpoint = capabilities.Descendants().FirstOrDefault(x => x.Name.LocalName == "Media" && x.Elements().Any())?
            .Elements().FirstOrDefault(x => x.Name.LocalName == "XAddr")?.Value;
        if (!Uri.TryCreate(mediaEndpoint, UriKind.Absolute, out var mediaUri))
            mediaUri = new Uri(deviceServiceUrl);

        var profiles = await PostAsync(mediaUri.ToString(), new XElement(trt + "GetProfiles"),
            "http://www.onvif.org/ver10/media/wsdl/GetProfiles", cancellationToken);
        var profile = profiles.Descendants(trt + "Profiles").FirstOrDefault();
        var token = (string?)profile?.Attribute("token");
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidDataException("The camera returned no ONVIF media profiles.");

        var streamUri = await PostAsync(mediaUri.ToString(),
            new XElement(trt + "GetStreamUri",
                new XElement(trt + "StreamSetup",
                    new XElement(tt + "Stream", "RTP-Unicast"),
                    new XElement(tt + "Transport", new XAttribute("Protocol", "RTSP"))),
                new XElement(trt + "ProfileToken", token)),
            "http://www.onvif.org/ver10/media/wsdl/GetStreamUri", cancellationToken);
        var result = streamUri.Descendants().FirstOrDefault(x => x.Name.LocalName == "Uri")?.Value;
        if (!Uri.TryCreate(result, UriKind.Absolute, out var parsed) || parsed.Scheme != "rtsp")
            throw new InvalidDataException("The camera did not return a valid RTSP stream URI.");
        return parsed.ToString();
    }

    private async Task<XDocument> PostAsync(string endpoint, XElement body, string action, CancellationToken cancellationToken)
    {
        XNamespace soap = "http://www.w3.org/2003/05/soap-envelope";
        XNamespace wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        XNamespace wsu = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        var nonce = RandomNumberGenerator.GetBytes(20);
        var created = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var createdBytes = Encoding.UTF8.GetBytes(created);
        var passwordBytes = Encoding.UTF8.GetBytes(_password);
        var digestInput = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(nonce, 0, digestInput, 0, nonce.Length);
        Buffer.BlockCopy(createdBytes, 0, digestInput, nonce.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, digestInput, nonce.Length + createdBytes.Length, passwordBytes.Length);
        var passwordDigest = Convert.ToBase64String(SHA1.HashData(digestInput));
        var envelope = new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "s", soap),
            new XElement(soap + "Header",
                new XElement(wsse + "Security", new XAttribute(XNamespace.Xmlns + "wsse", wsse),
                    new XAttribute(soap + "mustUnderstand", "1"),
                    new XElement(wsse + "UsernameToken",
                        new XElement(wsse + "Username", _username),
                        new XElement(wsse + "Password",
                            new XAttribute("Type", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"),
                            passwordDigest),
                        new XElement(wsse + "Nonce",
                            new XAttribute("EncodingType", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"),
                            Convert.ToBase64String(nonce)),
                        new XElement(wsu + "Created", new XAttribute(XNamespace.Xmlns + "wsu", wsu), created)))),
            new XElement(soap + "Body", body)));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
        request.Content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("action", $"\"{action}\""));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var doc = XDocument.Parse(responseXml);
        var fault = doc.Descendants().FirstOrDefault(x => x.Name.LocalName == "Fault");
        if (fault is not null) throw new InvalidOperationException(fault.Value.Trim());
        return doc;
    }

    public void Dispose() => _http.Dispose();
}
