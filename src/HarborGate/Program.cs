using HarborGate.Certificates;
using HarborGate.Configuration;
using HarborGate.Docker;
using HarborGate.Middleware;
using HarborGate.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

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

// Override OIDC settings with environment variables if present
if (builder.Configuration.GetValue<bool?>("HARBORGATE_OIDC_ENABLED") is { } oidcEnabled)
{
    harborGateOptions.Oidc.Enabled = oidcEnabled;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_OIDC_AUTHORITY")))
{
    harborGateOptions.Oidc.Authority = builder.Configuration.GetValue<string>("HARBORGATE_OIDC_AUTHORITY")!;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CLIENT_ID")))
{
    harborGateOptions.Oidc.ClientId = builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CLIENT_ID")!;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CLIENT_SECRET")))
{
    harborGateOptions.Oidc.ClientSecret = builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CLIENT_SECRET")!;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CALLBACK_PATH")))
{
    harborGateOptions.Oidc.CallbackPath = builder.Configuration.GetValue<string>("HARBORGATE_OIDC_CALLBACK_PATH")!;
}
if (!string.IsNullOrEmpty(builder.Configuration.GetValue<string>("HARBORGATE_OIDC_ROLE_CLAIM_TYPE")))
{
    harborGateOptions.Oidc.RoleClaimType = builder.Configuration.GetValue<string>("HARBORGATE_OIDC_ROLE_CLAIM_TYPE")!;
}
if (builder.Configuration.GetValue<bool?>("HARBORGATE_OIDC_SAVE_TOKENS") is { } saveTokens)
{
    harborGateOptions.Oidc.SaveTokens = saveTokens;
}
if (builder.Configuration.GetValue<bool?>("HARBORGATE_OIDC_REQUIRE_HTTPS_METADATA") is { } requireHttpsMetadata)
{
    harborGateOptions.Oidc.RequireHttpsMetadata = requireHttpsMetadata;
}

// Register the options for dependency injection
builder.Services.AddSingleton(harborGateOptions);
builder.Services.Configure<HarborGateOptions>(options =>
{
    options.DockerSocket = harborGateOptions.DockerSocket;
    options.HttpPort = harborGateOptions.HttpPort;
    options.HttpsPort = harborGateOptions.HttpsPort;
    options.EnableHttps = harborGateOptions.EnableHttps;
    options.RedirectHttpToHttps = harborGateOptions.RedirectHttpToHttps;
    options.LogLevel = harborGateOptions.LogLevel;
    options.Ssl = harborGateOptions.Ssl;
    options.Oidc = harborGateOptions.Oidc;
});

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register HTTP challenge store for ACME
builder.Services.AddSingleton<IHttpChallengeStore, HttpChallengeStore>();

builder.Services.AddSingleton<OidcProviderValidator>();

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
        
        try
        {
            return new LetsEncryptCertificateProvider(storage, logger, harborGateOptions.Ssl.LetsEncrypt, challengeStore);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"error: ✗ Let's Encrypt configuration error: {ex.Message}");
            Environment.Exit(1);
            throw; // Never reached, but required for compiler
        }
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

// Configure Authentication and Authorization
if (harborGateOptions.Oidc.Enabled)
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "Cookies";
        options.DefaultChallengeScheme = "OpenIdConnect";
    })
    .AddCookie("Cookies", options =>
    {
        options.Cookie.Name = "HarborGate.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOpenIdConnect("OpenIdConnect", options =>
    {
        options.Authority = harborGateOptions.Oidc.Authority;
        options.ClientId = harborGateOptions.Oidc.ClientId;
        options.ClientSecret = harborGateOptions.Oidc.ClientSecret;
        options.ResponseType = "code";
        options.CallbackPath = harborGateOptions.Oidc.CallbackPath;
        options.SaveTokens = harborGateOptions.Oidc.SaveTokens;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.RequireHttpsMetadata = harborGateOptions.Oidc.RequireHttpsMetadata;
        
        // Configure scopes
        options.Scope.Clear();
        foreach (var scope in harborGateOptions.Oidc.Scopes)
        {
            options.Scope.Add(scope);
        }
        
        // Map role claim type
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            RoleClaimType = harborGateOptions.Oidc.RoleClaimType,
            NameClaimType = "name"
        };
    });

    builder.Services.AddAuthorization();
    
    // Register authorization handler
    builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<HarborGate.Authorization.RoleRequirementHandler>>();
        return new HarborGate.Authorization.RoleRequirementHandler(logger, harborGateOptions.Oidc.RoleClaimType);
    });
}

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

// Validate OIDC provider if enabled
if (harborGateOptions.Oidc.Enabled)
{
    app.Logger.LogInformation("Validating OIDC provider configuration...");
    
    var validator = app.Services.GetRequiredService<OidcProviderValidator>();
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    
    try
    {
        var validationResult = await validator.ValidateAsync(harborGateOptions.Oidc, cts.Token);
        
        if (validationResult.IsValid)
        {
            app.Logger.LogInformation("✓ OIDC provider validation successful");
        }
        else
        {
            var errorMessage = string.Join("\n  - ", validationResult.Errors);
            app.Logger.LogError("✗ OIDC provider validation FAILED. Errors:\n  - {Errors}", errorMessage);
            app.Logger.LogError("Application will now exit due to invalid OIDC configuration.");
            Environment.Exit(1);
        }
        
        // Log warnings if any
        foreach (var warning in validationResult.Warnings)
        {
            app.Logger.LogWarning("⚠ OIDC validation warning: {Warning}", warning);
        }
    }
    catch (OperationCanceledException)
    {
        app.Logger.LogError("✗ OIDC provider validation timed out after 30 seconds. Application will now exit.");
        Environment.Exit(1);
    }
}

// Add HTTPS redirect middleware (must be before ACME challenge)
// ACME challenge middleware will prevent redirects for /.well-known/acme-challenge/*
app.UseHttpsRedirect();

// Add ACME challenge middleware (must be early in pipeline, before YARP)
app.UseAcmeChallenge();

// Add authentication and authorization middleware (if OIDC is enabled)
if (harborGateOptions.Oidc.Enabled)
{
    app.Logger.LogInformation("OIDC is ENABLED - Adding authentication middleware");
    app.UseAuthentication();
    app.UseAuthorization();
    
    // Add conditional authentication middleware (must be after UseAuthentication/UseAuthorization)
    app.UseConditionalAuthentication();
    app.Logger.LogInformation("Conditional authentication middleware added to pipeline");
}
else
{
    app.Logger.LogWarning("OIDC is DISABLED - Authentication middleware NOT added. Routes with auth.enable=true will NOT be protected!");
}

// Health check endpoint (must be before MapReverseProxy to take precedence)
app.MapGet("/_health", () => Results.Json(new
{
    service = "Harbor Gate",
    status = "running",
    version = "0.1.0-phase4",
    https = harborGateOptions.EnableHttps,
    oidc = harborGateOptions.Oidc.Enabled
}));

// Use YARP reverse proxy - this will handle all other requests
app.MapReverseProxy();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Harbor Gate starting on HTTP port {HttpPort}", harborGateOptions.HttpPort);
if (harborGateOptions.EnableHttps)
{
    logger.LogInformation("Harbor Gate starting on HTTPS port {HttpsPort}", harborGateOptions.HttpsPort);
    logger.LogInformation("HTTP to HTTPS redirect: {Enabled}", harborGateOptions.RedirectHttpToHttps ? "Enabled" : "Disabled");
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

if (harborGateOptions.Oidc.Enabled)
{
    logger.LogInformation("OpenID Connect Authentication: Enabled");
    logger.LogInformation("OIDC Authority: {Authority}", harborGateOptions.Oidc.Authority);
    logger.LogInformation("OIDC Client ID: {ClientId}", harborGateOptions.Oidc.ClientId);
    logger.LogInformation("OIDC Callback Path: {CallbackPath}", harborGateOptions.Oidc.CallbackPath);
    logger.LogInformation("OIDC Require HTTPS Metadata: {RequireHttps}", harborGateOptions.Oidc.RequireHttpsMetadata);
    
    if (!harborGateOptions.Oidc.RequireHttpsMetadata)
    {
        logger.LogWarning("OIDC HTTPS requirement is DISABLED. This should only be used for development/testing!");
    }
}
else
{
    logger.LogInformation("OpenID Connect Authentication: Disabled");
}

app.Run();

// Helper class to hold the certificate selector and avoid closure warnings
sealed class CertificateSelectorHolder
{
    public DynamicCertificateSelector? Selector { get; set; }
}
