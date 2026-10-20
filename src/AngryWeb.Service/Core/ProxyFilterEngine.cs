using System.Net;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace AngryWeb.Service.Core;

public class ProxyFilterEngine : IDisposable
{
    private readonly ILogger<ProxyFilterEngine> _logger;
    private readonly ConfigManager _configManager;
    private readonly PatternMatcher _patternMatcher;
    private readonly TemporaryAccessManager _accessManager;
    private readonly ProxyServer _proxyServer;
    private ExplicitProxyEndPoint? _explicitEndPoint;

    public ProxyFilterEngine(
        ILogger<ProxyFilterEngine> logger,
        ConfigManager configManager,
        PatternMatcher patternMatcher,
        TemporaryAccessManager accessManager)
    {
        _logger = logger;
        _configManager = configManager;
        _patternMatcher = patternMatcher;
        _accessManager = accessManager;

        _proxyServer = new ProxyServer();
        // Trust Root Certificate automatically on Windows
        _proxyServer.CertificateManager.CertificateValidDays = 365;
        _proxyServer.CertificateManager.RootCertificateName = "Angry Web Filter Root CA";
        _proxyServer.CertificateManager.RootCertificateIssuerName = "Angry Web Security Engine";

        _proxyServer.BeforeRequest += OnBeforeRequest;
        _proxyServer.BeforeResponse += OnBeforeResponse;
        _proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
    }

    public void Start()
    {
        var config = _configManager.CurrentConfig;

        // Ensure Root Certificate is trusted
        try
        {
            _proxyServer.CertificateManager.CreateRootCertificate();
            _proxyServer.CertificateManager.TrustRootCertificate(true);
            _logger.LogInformation("Root Certificate installed and trusted.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to trust root certificate automatically. Admin privileges may be needed.");
        }

        _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Any, config.ProxyPort, true);
        _proxyServer.AddEndPoint(_explicitEndPoint);
        _proxyServer.Start();

        // Set as Windows system proxy
        try
        {
            _proxyServer.SetAsSystemProxy(_explicitEndPoint, ProxyProtocolType.AllHttp);
            _logger.LogInformation("AngryWeb proxy running on port {Port} and configured as System Proxy.", config.ProxyPort);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set system proxy automatically.");
        }
    }

    public void Stop()
    {
        try
        {
            if (_explicitEndPoint != null)
            {
                _proxyServer.DisableSystemProxy(ProxyProtocolType.AllHttp);
            }
            _proxyServer.Stop();
            _logger.LogInformation("Proxy stopped and system proxy settings restored.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping proxy server.");
        }
    }

    private Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
    {
        // Accept valid upstream certificates
        if (e.SslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
        {
            e.IsValid = true;
        }
        return Task.CompletedTask;
    }

    private async Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        var fullUrl = e.HttpClient.Request.Url;
        var host = e.HttpClient.Request.RequestUri.Host;

        // Skip internal/local requests
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Check if temporarily allowed by password bypass
        if (_accessManager.IsAllowed(fullUrl) || _accessManager.IsAllowed(host))
        {
            return;
        }

        // Check against PatternMatcher
        if (_patternMatcher.IsBlocked(fullUrl, out var matchedRule))
        {
            _logger.LogInformation("[BLOCKED] {Url} matched rule: {Rule}", fullUrl, matchedRule);

            // Redirect to local block page
            var blockPort = _configManager.CurrentConfig.BlockPagePort;
            var redirectUrl = $"http://127.0.0.1:{blockPort}/?target={Uri.EscapeDataString(fullUrl)}&rule={Uri.EscapeDataString(matchedRule ?? "")}";

            // If HTML navigation, redirect with 302
            if (e.HttpClient.Request.Method == "GET" &&
                (e.HttpClient.Request.Headers.HeaderExists("Accept") &&
                 e.HttpClient.Request.Headers.Headers["Accept"].Value.Contains("text/html")))
            {
                e.Ok($"<html><head><meta http-equiv='refresh' content='0;url={redirectUrl}'></head><body>Redirecting...</body></html>");
                return;
            }

            // Or respond with custom 403 HTML directly
            e.GenericResponse(
                $"<html><head><meta http-equiv='refresh' content='0;url={redirectUrl}'></head><body><h1>Blocked by Angry Web</h1></body></html>",
                HttpStatusCode.Forbidden,
                new Dictionary<string, HttpHeader>
                {
                    { "Location", new HttpHeader("Location", redirectUrl) },
                    { "Content-Type", new HttpHeader("Content-Type", "text/html; charset=utf-8") }
                });
        }
    }

    private Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Stop();
        _proxyServer.Dispose();
    }
}
