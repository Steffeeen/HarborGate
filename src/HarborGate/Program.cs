using HarborGate.Certificates;
using HarborGate.Configuration;
using HarborGate.Docker;
using HarborGate.Middleware;
using HarborGate.Services;

var builder = WebApplication.CreateBuilder(args);

// Get configuration for the entire application
var harborGateOptions = new HarborGateOptions();
builder.Configuration.GetSection(HarborGateOptions.SectionName).Bind(harborGateOptions);

// Override with environment variables if present
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_DOCKER_SOCKET")))
{
    harborGateOptions.DockerSocket = builder.Configuration.GetValue<string>("HARBORGATE_DOCKER_SOCKET")!;
}
if (builder.Configuration.GetValue<int?>("HARBORGATE_HTTP_PORT") is { } httpPort)
{
    harborGateOptions.HttpPort = httpPort;
}
if (builder.Configuration.GetValue<int?>("HARBORGATE_HTTPS_PORT") is { } httpsPort)
{
    harborGateOptions.HttpsPort = httpsPort;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_LOG_LEVEL")))
{
    harborGateOptions.LogLevel = builder.Configuration.GetValue<string>("HARBORGATE_LOG_LEVEL")!;
}

// Register the options for dependency injection
builder.Services.Configure<HarborGateOptions>(options =>
{
    options.DockerSocket = harborGateOptions.DockerSocket;
    options.HttpPort = harborGateOptions.HttpPort;
    options.HttpsPort = harborGateOptions.HttpsPort;
    options.EnableHttps = harborGateOptions.EnableHttps;
    options.LogLevel = harborGateOptions.LogLevel;
    options.Ssl = harborGateOptions.Ssl;
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register HTTP challenge store for ACME
builder.Services.AddSingleton<IHttpChallengeStore, HttpChallengeStore>();

// Register certificate storage service
builder.Services.AddSingleton(sp =>
{
    var logger = sp.GetRequiredService<ILogger<CertificateStorageService>>();
    var storagePath = harborGateOptions.Ssl.CertificateStoragePath;
    return new CertificateStorageService(logger, storagePath);
});

// Register certificate provider based on configuration
builder.Services.AddSingleton<ICertificateProvider>(sp =>
{
    var storage = sp.GetRequiredService<CertificateStorageService>();
    var providerType = harborGateOptions.Ssl.CertificateProvider;
    
    if (providerType.Equals("SelfSigned", StringComparison.OrdinalIgnoreCase))
    {
        var logger = sp.GetRequiredService<ILogger<SelfSignedCertificateProvider>>();
        return new SelfSignedCertificateProvider(storage, logger);
    }

    if (providerType.Equals("LetsEncrypt", StringComparison.OrdinalIgnoreCase))
    {
        var logger = sp.GetRequiredService<ILogger<LetsEncryptCertificateProvider>>();
        var challengeStore = sp.GetRequiredService<IHttpChallengeStore>();
        return new LetsEncryptCertificateProvider(storage, logger, harborGateOptions.Ssl.LetsEncrypt, challengeStore);
    }

    throw new InvalidOperationException($"Unknown certificate provider: {providerType}");
});

// Register dynamic certificate selector
builder.Services.AddSingleton<DynamicCertificateSelector>();

// Register services
builder.Services.AddSingleton<RouteConfigurationService>();

// Register Docker client wrapper
builder.Services.AddSingleton<IDockerClientWrapper>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<DockerClientWrapper>>();
    var dockerSocket = builder.Configuration.GetValue<string>("HARBORGATE_DOCKER_SOCKET") 
                      ?? "/var/run/docker.sock";
    return new DockerClientWrapper(dockerSocket, logger);
});

// Register Docker monitor service
builder.Services.AddHostedService<DockerMonitorService>();

// Register certificate renewal service
builder.Services.AddHostedService<CertificateRenewalService>();

// Register our custom configuration provider FIRST
builder.Services.AddSingleton<Yarp.ReverseProxy.Configuration.IProxyConfigProvider>(sp => 
    sp.GetRequiredService<RouteConfigurationService>());

// Add YARP reverse proxy - it will use our registered IProxyConfigProvider
builder.Services.AddReverseProxy();

// Configure Kestrel with certificate selection
// Note: We use a holder class to avoid closure warnings while still preventing
// duplicate singleton instances via BuildServiceProvider()
var certificateSelectorHolder = new CertificateSelectorHolder();

builder.WebHost.ConfigureKestrel((_, options) =>
{
    // Listen on HTTP
    options.ListenAnyIP(harborGateOptions.HttpPort);
    
    // Listen on HTTPS if enabled
    if (harborGateOptions.EnableHttps)
    {
        options.ListenAnyIP(harborGateOptions.HttpsPort, listenOptions =>
        {
            listenOptions.UseHttps(httpsOptions =>
            {
                // ServerCertificateSelector is called for each TLS connection
                httpsOptions.ServerCertificateSelector = (connectionContext, hostname) =>
                {
                    if (string.IsNullOrEmpty(hostname))
                    {
                        return null;
                    }

                    // Use the certificate selector from the app's service provider
                    // This avoids creating duplicate singleton instances
                    var selector = certificateSelectorHolder.Selector;

                    // Select certificate based on hostname (synchronous wrapper)
                    return selector?.SelectCertificateAsync(connectionContext, hostname)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                };
            });
        });
    }
});

var app = builder.Build();

// Initialize certificate selector after app is built
if (harborGateOptions.EnableHttps)
{
    certificateSelectorHolder.Selector = app.Services.GetRequiredService<DynamicCertificateSelector>();
}

// Load existing certificates from disk on startup
var storage = app.Services.GetRequiredService<CertificateStorageService>();
await storage.LoadCertificatesFromDiskAsync();

// Add ACME challenge middleware (must be early in pipeline, before YARP)
app.UseAcmeChallenge();

// Health check endpoint (must be before MapReverseProxy to take precedence)
app.MapGet("/_health", () => Results.Json(new
{
    service = "Harbor Gate",
    status = "running",
    version = "0.1.0-phase3",
    https = harborGateOptions.EnableHttps
}));

// Use YARP reverse proxy - this will handle all other requests
app.MapReverseProxy();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Harbor Gate starting on HTTP port {HttpPort}", harborGateOptions.HttpPort);
if (harborGateOptions.EnableHttps)
{
    logger.LogInformation("Harbor Gate starting on HTTPS port {HttpsPort}", harborGateOptions.HttpsPort);
    logger.LogInformation("SSL Certificate Provider: {Provider}", harborGateOptions.Ssl.CertificateProvider);
    
    if (harborGateOptions.Ssl.CertificateProvider.Equals("LetsEncrypt", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Let's Encrypt Email: {Email}", harborGateOptions.Ssl.LetsEncrypt.Email);
        logger.LogInformation("Let's Encrypt ACME Directory: {Directory}", 
            harborGateOptions.Ssl.LetsEncrypt.AcmeDirectoryUrl ?? 
            (harborGateOptions.Ssl.LetsEncrypt.UseStaging ? "Staging" : "Production"));
        logger.LogInformation("Skip ACME Server Certificate Validation: {Skip}", 
            harborGateOptions.Ssl.LetsEncrypt.SkipAcmeServerCertificateValidation);
    }
}

app.Run();

// Helper class to hold the certificate selector and avoid closure warnings
sealed class CertificateSelectorHolder
{
    public DynamicCertificateSelector? Selector { get; set; }
}
