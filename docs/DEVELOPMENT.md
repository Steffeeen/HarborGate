# Development Guide

This document covers local development, testing, architecture, and implementation details for Harbor Gate.

## Table of Contents

- [Local Development](#local-development)
- [Testing](#testing)
- [SSL/TLS Configuration](#ssltls-configuration)
- [Authentication](#authentication)
- [Architecture](#architecture)

## Local Development

### Running Harbor Gate Locally

**Option 1: Command Line**

```bash
cd src/HarborGate
dotnet run
```

Harbor Gate starts on port 8080 (configured in `appsettings.Development.json`).

**Option 2: With Hot Reload**

```bash
dotnet watch run --project src/HarborGate
```

Automatically restarts on code changes.

**Option 3: IDE**

- **VS Code**: Press F5 or use Run > Start Debugging
- **Rider/Visual Studio**: Open `HarborGate.sln` and press F5

### Setting Up Test Containers

Start test containers for local development:

```bash
# Create network
docker network create harborgate-local

# Start test containers
docker run -d \
  --name test-whoami-1 \
  --network harborgate-local \
  -p 8081:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=whoami1.localhost" \
  traefik/whoami

docker run -d \
  --name test-whoami-2 \
  --network harborgate-local \
  -p 8082:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=whoami2.localhost" \
  traefik/whoami
```

Or use the convenience script:

```bash
./scripts/start-test-containers.sh
```

### Testing Routes

```bash
# Test route 1
curl -H "Host: whoami1.localhost" http://localhost:8080

# Test route 2
curl -H "Host: whoami2.localhost" http://localhost:8080
```

### Local Configuration

`appsettings.Development.json` is pre-configured for local development:

```json
{
  "HarborGate": {
    "DockerSocket": "/var/run/docker.sock",
    "HttpPort": 8080,
    "HttpsPort": 8443,
    "LogLevel": "Debug"
  }
}
```

**Key differences from production:**
- Port 8080 instead of 80 (no admin privileges needed)
- Debug logging for detailed output
- Same Docker socket access as containerized version

### Environment Variable Overrides

```bash
HARBORGATE_HTTP_PORT=9000 \
HARBORGATE_LOG_LEVEL=Information \
dotnet run --project src/HarborGate
```

### Testing Dynamic Updates

```bash
# Add container
docker run -d \
  --name dynamic-test \
  --network harborgate-local \
  --label "harborgate.enable=true" \
  --label "harborgate.host=dynamic.localhost" \
  traefik/whoami

# Test route
curl -H "Host: dynamic.localhost" http://localhost:8080

# Remove container
docker stop dynamic-test && docker rm dynamic-test
```

Watch Harbor Gate console output for route add/remove messages.

## Testing

### Building

```bash
# Build project
dotnet build

# Build Docker image
docker build -t harborgate:latest -f src/HarborGate/Dockerfile .
```

### Running E2E Tests

Harbor Gate includes comprehensive E2E test suites covering routing, SSL, authentication, and WebSockets.

> [!NOTE]
> Running the full test suite takes approximately 20 minutes.

**Run all tests:**
```bash
cd tests
./run-tests.sh all
```

**Run specific test suite:**
```bash
./run-tests.sh routing      # Routing tests (7 tests)
./run-tests.sh ssl          # SSL/TLS tests (7 tests)
./run-tests.sh auth         # Authentication tests (8 tests)
./run-tests.sh websocket    # WebSocket tests (7 tests)
```

**Test suites:**

- **Routing Tests**: Basic routing, port discovery, dynamic updates, host header matching
- **SSL Tests**: Self-signed certificates, Let's Encrypt (via Pebble), HTTP→HTTPS redirect, SNI
- **Authentication Tests**: OIDC integration, role-based access control, callback handling
- **WebSocket Tests**: Connection establishment, message echo, long-lived connections, invalid host rejection

Each test suite:
1. Starts isolated Docker environment
2. Runs Harbor Gate with specific configuration
3. Executes tests
4. Cleans up containers and networks

### Development Testing

Test with docker-compose:

```bash
# Start development environment
docker-compose -f docker-compose.dev.yml up --build

# Test routes
curl -H "Host: whoami1.localhost" http://localhost:8080

# View logs
docker logs harborgate-dev -f
```

### Test Checklist

- [ ] Harbor Gate builds successfully
- [ ] Docker image builds successfully
- [ ] All E2E tests pass (29/29)
- [ ] Routes discovered on startup
- [ ] HTTP requests proxied correctly
- [ ] New containers detected dynamically
- [ ] Stopped containers removed from routes
- [ ] Port auto-discovery works
- [ ] WebSocket upgrade works
- [ ] SSL certificates issued correctly
- [ ] OIDC authentication flow works

## SSL/TLS Configuration

### Self-Signed Certificates (Development)

Perfect for local testing. Certificates generated automatically per hostname.

**Configuration:**

appsettings.json:
```json
{
  "HarborGate": {
    "EnableHttps": true,
    "HttpsPort": 8443,
    "Ssl": {
      "CertificateProvider": "SelfSigned",
      "CertificateStoragePath": "./certs"
    }
  }
}
```

**Usage:**

```bash
dotnet run
curl -k --resolve whoami.localhost:8443:127.0.0.1 https://whoami.localhost:8443
```

**Certificate properties:**
- Valid for 1 year
- Automatically regenerated when expired
- Stored as `{hostname}.pfx`
- Includes proper Subject Alternative Names (SAN)

### Let's Encrypt (Production)

Automatic certificate issuance and renewal.

**Configuration:**

appsettings.json:
```json
{
  "HarborGate": {
    "EnableHttps": true,
    "HttpPort": 80,
    "HttpsPort": 443,
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "CertificateStoragePath": "/var/lib/harborgate/certs",
      "LetsEncrypt": {
        "Email": "your-email@example.com",
        "UseStaging": false,
        "AcceptTermsOfService": true
      }
    }
  }
}
```

**Requirements:**
- Publicly accessible server with ports 80 and 443 open
- Valid DNS records pointing to server
- Accept Let's Encrypt Terms of Service

**Environment variables:**

```bash
HARBORGATE_ENABLE_HTTPS=true
HARBORGATE_ACME_EMAIL=your-email@example.com
HARBORGATE_ACME_ACCEPT_TOS=true
```

### Certificate Lifecycle

**Automatic Issuance:**

1. Client connects via SNI with hostname
2. Harbor Gate checks for cached certificate
3. If not found, initiates ACME HTTP-01 challenge
4. Let's Encrypt validates domain ownership
5. Certificate issued and stored
6. Future requests use cached certificate

**Automatic Renewal:**

`CertificateRenewalService` runs every 12 hours:
- Checks all stored certificates
- Renews certificates expiring within 30 days
- Updates cached certificates automatically
- No downtime or manual intervention

**Certificate Storage:**

Format: PKCS#12 (.pfx)
Location: Configured via `CertificateStoragePath`
Filename: `{hostname}.pfx`

Example:
```
/var/lib/harborgate/certs/
├── app1.example.com.pfx
├── app2.example.com.pfx
└── api.example.com.pfx
```

### Testing with Pebble

Pebble is Let's Encrypt's ACME test server for local development.

**Start Pebble environment:**

```bash
docker-compose -f docker-compose.pebble.yml up -d
```

This starts:
- Pebble ACME server on port 14000
- Harbor Gate with Pebble configuration
- Test applications

**Configuration (`appsettings.Pebble.json`):**

```json
{
  "HarborGate": {
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "LetsEncrypt": {
        "Email": "test@harborgate.local",
        "AcmeDirectoryUrl": "https://pebble:14000/dir",
        "AcceptTermsOfService": true,
        "SkipAcmeServerCertificateValidation": true
      }
    }
  }
}
```

**Important:** `SkipAcmeServerCertificateValidation: true` is required for Pebble testing only. Never use in production.

**Test:**

```bash
curl -k https://localhost/ -H "Host: app1.test.local"
```

**Monitoring:**

```bash
# Harbor Gate logs
docker logs -f harborgate-pebble-test

# Pebble logs
docker logs -f pebble

# Check certificates
ls -la certs-pebble/
```

### Troubleshooting SSL

**Certificate not generated:**
1. Check provider configuration
2. Review logs for certificate request errors
3. For Let's Encrypt: Ensure port 80 accessible for HTTP-01 challenge

```bash
docker logs harborgate | grep -i certificate
curl http://yourdomain.com/.well-known/acme-challenge/test
```

**HTTP-01 challenge fails:**

Common causes:
- Port 80 not accessible from internet
- Firewall blocking HTTP traffic
- DNS not pointing to correct server

Solutions:
```bash
# Test external accessibility
curl http://yourdomain.com/.well-known/acme-challenge/test

# Check port binding
sudo lsof -i :80

# Verify DNS
dig yourdomain.com
```

**Let's Encrypt rate limits:**

Use staging environment for testing:
```json
{
  "LetsEncrypt": {
    "UseStaging": true
  }
}
```

See [Let's Encrypt Rate Limits](https://letsencrypt.org/docs/rate-limits/)

## Authentication

Harbor Gate supports OpenID Connect (OIDC) with role-based access control (RBAC) on a per-route basis.

### Quick Setup

**1. Configure OIDC Provider**

Environment variables:
```bash
HARBORGATE_OIDC_ENABLED=true
HARBORGATE_OIDC_AUTHORITY=https://your-idp.com
HARBORGATE_OIDC_CLIENT_ID=your-client-id
HARBORGATE_OIDC_CLIENT_SECRET=your-client-secret
```

Or `appsettings.json`:
```json
{
  "HarborGate": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://your-idp.com",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "CallbackPath": "/signin-oidc",
      "Scopes": ["openid", "profile", "email"],
      "RoleClaimType": "roles"
    }
  }
}
```

**2. Protect Routes**

Add authentication labels to containers:

```yaml
protected-app:
  image: myapp:latest
  labels:
    - "harborgate.enable=true"
    - "harborgate.host=app.example.com"
    - "harborgate.auth.enable=true"
    - "harborgate.auth.roles=admin,user"  # Optional: require specific roles
```

### Authentication Flow

1. **Unauthenticated Request**: User requests protected route
2. **Redirect to IDP**: Harbor Gate redirects to OIDC provider
3. **User Login**: User authenticates with OIDC provider
4. **Callback**: OIDC provider redirects back with authorization code
5. **Token Exchange**: Harbor Gate exchanges code for tokens
6. **Session Created**: User session stored in encrypted cookie
7. **Authorization Check**: Harbor Gate checks user roles against required roles
8. **Access Granted/Denied**: User forwarded to backend or receives 403 Forbidden

### Role-Based Access Control

**How roles work:**
- Routes can require one or more roles via `harborgate.auth.roles`
- Multiple roles (comma-separated): **any one role grants access** (OR logic)
- No roles specified: any authenticated user can access
- Roles read from JWT token claim specified by `RoleClaimType`

**Scenarios:**

Authentication only (no role required):
```yaml
labels:
  - "harborgate.auth.enable=true"
```

Admin only:
```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.auth.roles=admin"
```

Multiple roles (OR logic):
```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.auth.roles=admin,editor,viewer"
```

Public route (no authentication):
```yaml
labels:
  - "harborgate.enable=true"
  - "harborgate.host=public.example.com"
```

### Startup Validation

Harbor Gate validates OIDC provider configuration at startup (30-second timeout):

1. Fetches `{Authority}/.well-known/openid-configuration`
2. Verifies required endpoints exist (authorization, token, userinfo, jwks)
3. Ensures provider is reachable
4. Validates client credentials format

**If validation fails, the application exits immediately** to prevent running with broken authentication.

**Successful validation:**
```
info: Harbor Gate[0] Validating OIDC provider configuration...
info: Harbor Gate[0] ✓ OIDC provider validation successful
```

**Failed validation:**
```
error: Harbor Gate[0] ✗ OIDC provider validation FAILED. Errors:
  - Failed to connect to OIDC provider: Connection refused
error: Harbor Gate[0] Application will now exit due to invalid OIDC configuration.
```

### OIDC Providers

Harbor Gate supports any standard OpenID Connect provider:

**Keycloak:**
```json
{
  "Authority": "https://keycloak.example.com/realms/myrealm",
  "ClientId": "harborgate",
  "ClientSecret": "your-secret"
}
```

**Auth0:**
```json
{
  "Authority": "https://your-tenant.auth0.com",
  "ClientId": "your-client-id",
  "ClientSecret": "your-secret"
}
```

**Azure AD / Microsoft Entra ID:**
```json
{
  "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "ClientId": "your-application-id",
  "ClientSecret": "your-secret"
}
```

**Google:**
```json
{
  "Authority": "https://accounts.google.com",
  "ClientId": "your-client-id.apps.googleusercontent.com",
  "ClientSecret": "your-secret"
}
```

### Callback URL Configuration

When configuring your OIDC provider, set the callback/redirect URI to:

```
https://your-harbor-gate-domain/signin-oidc
```

Example: `https://gateway.example.com/signin-oidc`

### Security Considerations

**Cookie Security:**
- Session cookies encrypted using ASP.NET Core Data Protection
- HTTP-only (not accessible via JavaScript)
- `SameSite=Lax` to prevent CSRF attacks
- Secure when HTTPS enabled

**Token Storage:**
- By default, tokens NOT stored (`SaveTokens: false`)
- Enable `SaveTokens: true` only if backend needs access to tokens
- Tokens encrypted if stored in session

**HTTPS Requirement:**
- Always use HTTPS in production with OIDC authentication
- Most OIDC providers require HTTPS for callbacks
- Session cookies should only be transmitted over HTTPS

### Troubleshooting Authentication

**Authentication redirect loop:**

Solutions:
1. Verify callback URL correctly configured in OIDC provider
2. Check `CallbackPath` matches OIDC provider configuration
3. Ensure HTTPS enabled if required by OIDC provider
4. Check browser cookies enabled

**403 Forbidden after login:**

Solutions:
1. Check user has required roles in OIDC provider
2. Verify `RoleClaimType` matches claim name in JWT token
3. Check Harbor Gate logs for role authorization messages
4. Inspect JWT token to verify roles present (use jwt.io)

**Invalid issuer/signature errors:**

Solutions:
1. Verify `Authority` URL correct and accessible from Harbor Gate
2. Check OIDC provider using HTTPS
3. Ensure Harbor Gate can reach OIDC provider's metadata endpoint
4. Check for clock skew between Harbor Gate and OIDC provider

**Missing roles in token:**

Solutions:
1. Configure OIDC provider to include roles in ID token or access token
2. Set `GetClaimsFromUserInfoEndpoint: true` in configuration
3. Check `RoleClaimType` matches provider's role claim name
4. Some providers use different claim names (e.g., `role`, `roles`, `groups`)

### Testing Authentication

**Test with Keycloak (Docker):**

```yaml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:latest
    environment:
      - KEYCLOAK_ADMIN=admin
      - KEYCLOAK_ADMIN_PASSWORD=admin
    ports:
      - "8080:8080"
    command: start-dev

  harborgate:
    image: harborgate:latest
    depends_on:
      - keycloak
    environment:
      - HARBORGATE_OIDC_ENABLED=true
      - HARBORGATE_OIDC_AUTHORITY=http://keycloak:8080/realms/master
      - HARBORGATE_OIDC_CLIENT_ID=harborgate
      - HARBORGATE_OIDC_CLIENT_SECRET=secret
```

**Enable debug logging:**

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.Authentication": "Debug",
      "HarborGate.Authorization": "Debug",
      "HarborGate.Middleware": "Debug"
    }
  }
}
```

Look for log messages:
- `"User not authenticated for protected route"` - User needs to login
- `"User lacks required roles"` - Authorization failed
- `"User authorized for host"` - Authorization successful

## Architecture

### Key Components

**YARP (Yet Another Reverse Proxy):**
- Microsoft's reverse proxy library
- Handles HTTP/HTTPS/WebSocket proxying
- Dynamic route configuration
- Built-in load balancing and health checks

**Docker Monitor Service:**
- Monitors Docker socket for container events
- Scans existing containers on startup
- Parses `harborgate.*` labels
- Discovers exposed ports automatically
- Triggers route configuration updates

**Route Configuration Service:**
- Implements `IProxyConfigProvider` for YARP
- Converts Docker labels to YARP `RouteConfig` and `ClusterConfig`
- Maintains in-memory route cache
- Notifies YARP of route changes

**Certificate Services:**
- `SelfSignedCertificateProvider`: Generates self-signed certificates
- `LetsEncryptCertificateProvider`: ACME client for Let's Encrypt
- `CertificateRenewalService`: Background service for automatic renewal
- SNI-based certificate selection in TLS handshake

**Authentication Middleware:**
- OIDC authentication integration
- Per-route authorization based on container labels
- Role-based access control
- Session management with encrypted cookies

### Request Flow

1. **Client Request**: Client sends HTTP/HTTPS request with Host header
2. **TLS Handshake** (if HTTPS): Certificate selected based on SNI hostname
3. **Authentication Check**: Middleware checks if route requires authentication
4. **Authorization Check**: If authenticated, validates user roles
5. **Route Matching**: YARP matches Host header to configured routes
6. **Proxy Request**: YARP proxies request to backend container
7. **Response**: Backend response proxied back to client

### Code Structure

```
src/HarborGate/
├── Program.cs                      # Application entry point
├── Models/
│   └── RouteConfiguration.cs       # Route/cluster model from Docker labels
├── Services/
│   ├── DockerMonitorService.cs     # Docker event monitoring
│   ├── RouteConfigurationService.cs # YARP route provider
│   ├── CertificateRenewalService.cs # Background renewal service
│   └── Ssl/
│       ├── ICertificateProvider.cs
│       ├── SelfSignedCertificateProvider.cs
│       └── LetsEncryptCertificateProvider.cs
├── Middleware/
│   ├── AuthorizationMiddleware.cs  # Per-route RBAC
│   └── AcmeChallengeMiddleware.cs  # HTTP-01 challenge handler
└── Validators/
    └── OidcProviderValidator.cs     # Startup OIDC validation

tests/HarborGate.E2ETests/
├── RoutingTests.cs                  # E2E routing tests
├── SslTests.cs                      # E2E SSL/TLS tests
├── AuthenticationTests.cs           # E2E OIDC tests
└── WebSocketTests.cs                # E2E WebSocket tests
```

### Development Workflow

1. Make code changes
2. Test locally with `dotnet watch run`
3. Run E2E tests: `cd tests && ./run-tests.sh all`
4. Build Docker image
5. Test with docker-compose
6. Deploy

