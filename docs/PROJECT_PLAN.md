# Harbor Gate - Project Plan

## Overview

Harbor Gate is a reverse proxy built with C# and .NET, inspired by Traefik. It provides automatic service discovery via Docker labels, SSL certificate management through Let's Encrypt, and OpenID Connect authentication.

## Architecture

```
Harbor Gate Architecture
========================

┌─────────────────────────────────────────────────────────────┐
│                      Harbor Gate Container                   │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              ASP.NET Core + YARP                       │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  HTTP/HTTPS Request Handler                      │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  OpenID Connect Authentication Middleware        │  │ │
│  │  │  (Optional per route based on labels)            │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Dynamic Route Matching & Forwarding             │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │         Background Services (Hosted Services)         │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Docker Monitor Service                          │  │ │
│  │  │  - Watches Docker events                         │  │ │
│  │  │  - Parses container labels                       │  │ │
│  │  │  - Discovers exposed ports                       │  │ │
│  │  │  - Updates route configuration dynamically       │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  │  ┌──────────────────────────────────────────────────┐  │ │
│  │  │  Certificate Manager Service                     │  │ │
│  │  │  - Requests certificates from Let's Encrypt      │  │ │
│  │  │  - Handles ACME HTTP-01 challenges               │  │ │
│  │  │  - Monitors certificate expiration               │  │ │
│  │  │  - Auto-renews certificates                      │  │ │
│  │  └──────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              Configuration Storage                    │ │
│  │  - In-memory route configuration                     │ │
│  │  - Certificate storage (file system)                 │  │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
         │                              │
         │ Docker API                   │ HTTP/HTTPS
         ▼                              ▼
    Docker Socket              Backend Containers
    (/var/run/docker.sock)    (service1, service2, etc.)
```

## Technology Stack

- **.NET 10** - Latest .NET runtime and SDK
- **YARP (Yet Another Reverse Proxy)** - Microsoft's reverse proxy library built on ASP.NET Core
- **Docker.DotNet** - Official Docker API client for .NET
- **Certes** - ACME (Let's Encrypt) client library for .NET
- **Microsoft.AspNetCore.Authentication.OpenIdConnect** - Built-in OIDC authentication

## Label Schema

Harbor Gate uses Docker labels with the `harborgate.` prefix to configure routing:

```yaml
labels:
  # Basic routing (required)
  harborgate.enable: "true"
  harborgate.host: "service1.example.com"
  
  # Port discovery (optional - auto-discovers if not specified)
  harborgate.port: "8080"
  
  # SSL/TLS (optional - defaults to true if host is specified)
  harborgate.tls: "true"
  
  # Authentication (optional)
  harborgate.auth.enable: "true"
  harborgate.auth.roles: "admin,user"  # Required roles for RBAC
```

## Environment Variables

```bash
# Docker connection
HARBORGATE_DOCKER_SOCKET=/var/run/docker.sock

# Let's Encrypt (Phase 3)
HARBORGATE_ACME_EMAIL=admin@example.com
HARBORGATE_ACME_STAGING=false  # Use staging for testing
HARBORGATE_CERT_STORAGE=/app/certificates

# OpenID Connect (Phase 4)
HARBORGATE_OIDC_AUTHORITY=https://your-idp.com
HARBORGATE_OIDC_CLIENT_ID=harborgate
HARBORGATE_OIDC_CLIENT_SECRET=your-secret
HARBORGATE_OIDC_CALLBACK_PATH=/signin-oidc

# General
HARBORGATE_HTTP_PORT=80
HARBORGATE_HTTPS_PORT=443
HARBORGATE_LOG_LEVEL=Information
```

## Development Phases

### Phase 1: Foundation & Basic Reverse Proxy ✅

**Status**: Implemented

**Goal**: Get basic reverse proxy working with YARP

**Components**:
- ASP.NET Core project with YARP
- Basic reverse proxy functionality
- Configuration models
- In-memory route configuration
- Structured logging
- Dockerfile for containerization

**Key Files**:
- `Program.cs` - Main entry point with YARP setup
- `Services/RouteConfigurationService.cs` - Implements IProxyConfigProvider for dynamic routes
- `Models/RouteConfiguration.cs` - Route data models
- `Extensions/ServiceCollectionExtensions.cs` - Dependency injection setup

**Deliverable**: Harbor Gate can proxy HTTP requests based on configuration

---

### Phase 2: Docker Integration & Label-Based Configuration ✅

**Status**: Implemented

**Goal**: Dynamic route discovery from Docker containers

**Components**:
- Docker.DotNet integration
- Docker event monitoring
- Container label parsing
- Automatic port discovery
- Dynamic route hot-reloading

**Key Files**:
- `Services/DockerMonitorService.cs` - Watches Docker events and manages container lifecycle
- `Docker/DockerClientWrapper.cs` - Docker API abstraction
- `Docker/LabelParser.cs` - Parses Harbor Gate labels from containers
- `Models/ContainerInfo.cs` - Container data model
- `Models/HarborGateLabels.cs` - Structured label configuration
- `Configuration/HarborGateOptions.cs` - Application configuration options

**Port Discovery Logic**:
1. Check for `harborgate.port` label (highest priority)
2. If not found, check container's exposed ports
3. Use first exposed port
4. If no ports found, skip container and log error

**Route Configuration Flow**:
1. DockerMonitorService detects container with `harborgate.enable=true`
2. LabelParser extracts all `harborgate.*` labels
3. Discovers target port (label or auto-detect)
4. Gets container's internal IP and network
5. Builds YARP RouteConfig: `{Host} -> http://{ContainerIP}:{Port}`
6. Calls RouteConfigurationService.UpdateRoutes()
7. YARP hot-reloads configuration without restart

**Deliverable**: Harbor Gate dynamically configures routes based on Docker container labels with automatic port discovery

---

### Phase 3: SSL/TLS & Let's Encrypt Integration

**Status**: Planned

**Goal**: Automatic HTTPS with Let's Encrypt certificates

**Components**:
- Certes library for ACME protocol
- Certificate lifecycle management
- HTTP-01 challenge handling
- Certificate storage and persistence
- Automatic renewal (30 days before expiry)
- Dynamic HTTPS bindings with SNI support

**Key Files** (to be created):
- `Services/CertificateManagerService.cs` - Manages certificate lifecycle
- `Middleware/AcmeChallengeMiddleware.cs` - Handles `/.well-known/acme-challenge/` requests
- `Models/CertificateInfo.cs` - Certificate metadata
- `Certificates/CertificateStore.cs` - Certificate storage abstraction

**Technical Details**:
- Use HTTP-01 challenge (no DNS provider required)
- Store certificates in `/app/certificates` (Docker volume)
- Support Let's Encrypt staging environment for testing
- Configure Kestrel for dynamic HTTPS bindings
- Implement SNI (Server Name Indication) for multiple domains

**Certificate Request Flow**:
1. New container with `harborgate.tls=true` detected
2. Check if certificate exists for hostname
3. If not, initiate ACME challenge
4. Request challenge from Let's Encrypt
5. Respond to HTTP-01 challenge at `/.well-known/acme-challenge/{token}`
6. Receive and store certificate
7. Configure Kestrel HTTPS binding for hostname
8. Schedule renewal check

**Deliverable**: Harbor Gate automatically obtains and renews SSL certificates from Let's Encrypt

---

### Phase 4: OpenID Connect Authentication

**Status**: Planned

**Goal**: Protect routes with OIDC authentication and RBAC

**Components**:
- Microsoft OIDC middleware integration
- Per-route authentication enforcement
- Role-based access control (RBAC)
- Claims-based authorization
- Cookie-based sessions

**Key Files** (to be created):
- `Middleware/ConditionalAuthenticationMiddleware.cs` - Apply auth per route
- `Authorization/RoleRequirementHandler.cs` - RBAC authorization handler
- `Models/AuthConfig.cs` - Authentication configuration

**Technical Details**:
- Configure OIDC from environment variables
- Check `harborgate.auth.enable` label per route
- Parse `harborgate.auth.roles` for required roles
- Use ASP.NET Core authorization policies
- Handle authentication callbacks at `/signin-oidc`
- Store session in encrypted cookies

**Authentication Flow**:
1. Request arrives for route with `harborgate.auth.enable=true`
2. Check if user is authenticated (has valid session cookie)
3. If not, redirect to OIDC provider
4. User authenticates with OIDC provider
5. Callback to Harbor Gate with authorization code
6. Exchange code for tokens and extract claims
7. Validate required roles from `harborgate.auth.roles`
8. Create session cookie
9. Redirect to original URL
10. Subsequent requests use session cookie

**Deliverable**: Harbor Gate can protect routes with OIDC authentication and role-based access control

---

### Phase 5: Polish & Production Readiness

**Status**: Planned

**Goal**: Make Harbor Gate production-ready

**Components**:
- Comprehensive error handling
- Graceful shutdown
- Configuration validation
- Health check endpoints
- Structured logging improvements
- Documentation
- Integration tests
- Performance testing

**Tasks**:
1. Add health check endpoint (`/health`)
2. Add readiness check endpoint (`/ready`)
3. Implement graceful shutdown (drain connections)
4. Validate all configuration on startup
5. Improve error messages and logging
6. Add request/response logging middleware
7. Document all features in README
8. Create comprehensive docker-compose examples
9. Write integration tests
10. Perform load testing

**Documentation** (to be created):
- `README.md` - Quick start guide and feature overview
- `docs/CONFIGURATION.md` - Complete configuration reference
- `docs/EXAMPLES.md` - Real-world usage examples
- `docs/TROUBLESHOOTING.md` - Common issues and solutions
- `docs/ARCHITECTURE.md` - Deep dive into implementation

**Deliverable**: Production-ready Harbor Gate with comprehensive documentation and testing

---

## Project Structure

```
HarborGate/
├── .gitignore
├── README.md
├── PROJECT_PLAN.md
├── HarborGate.sln
├── docker-compose.example.yml
├── docker-compose.dev.yml
├── docs/                              # Phase 5
│   ├── CONFIGURATION.md
│   ├── EXAMPLES.md
│   ├── TROUBLESHOOTING.md
│   └── ARCHITECTURE.md
├── src/
│   └── HarborGate/
│       ├── HarborGate.csproj
│       ├── Program.cs
│       ├── Dockerfile
│       ├── .dockerignore
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       ├── Models/
│       │   ├── RouteConfiguration.cs      # Phase 1
│       │   ├── ContainerInfo.cs           # Phase 2
│       │   ├── HarborGateLabels.cs        # Phase 2
│       │   ├── CertificateInfo.cs         # Phase 3
│       │   └── AuthConfig.cs              # Phase 4
│       ├── Services/
│       │   ├── RouteConfigurationService.cs       # Phase 1
│       │   ├── DockerMonitorService.cs            # Phase 2
│       │   └── CertificateManagerService.cs       # Phase 3
│       ├── Docker/
│       │   ├── IDockerClientWrapper.cs    # Phase 2
│       │   ├── DockerClientWrapper.cs     # Phase 2
│       │   └── LabelParser.cs             # Phase 2
│       ├── Middleware/
│       │   ├── AcmeChallengeMiddleware.cs         # Phase 3
│       │   └── ConditionalAuthenticationMiddleware.cs  # Phase 4
│       ├── Configuration/
│       │   └── HarborGateOptions.cs       # Phase 2
│       ├── Certificates/                  # Phase 3
│       │   └── CertificateStore.cs
│       ├── Authorization/                 # Phase 4
│       │   └── RoleRequirementHandler.cs
│       └── Extensions/
│           └── ServiceCollectionExtensions.cs     # Phase 1
└── tests/
    └── HarborGate.Tests/                  # Phase 5
```

## Example Usage

Once fully implemented, here's how users will use Harbor Gate:

```yaml
# docker-compose.yml
version: '3.8'

services:
  harborgate:
    image: harborgate:latest
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./certificates:/app/certificates
    environment:
      - HARBORGATE_ACME_EMAIL=admin@example.com
      - HARBORGATE_OIDC_AUTHORITY=https://auth.example.com
      - HARBORGATE_OIDC_CLIENT_ID=harborgate
      - HARBORGATE_OIDC_CLIENT_SECRET=secret

  # Public web app with SSL
  webapp:
    image: nginx:alpine
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=webapp.example.com"
      - "harborgate.tls=true"

  # Protected API with authentication
  api:
    image: myapi:latest
    expose:
      - "8080"
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=api.example.com"
      - "harborgate.tls=true"
      - "harborgate.auth.enable=true"
      - "harborgate.auth.roles=admin,api-user"

  # Internal service without SSL
  internal:
    image: internal-service:latest
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=internal.localhost"
      - "harborgate.tls=false"
```

## Future Enhancements (Beyond Phase 5)

Potential features for future development:

1. **Load Balancing**
   - Multiple backend instances per route
   - Health checks for backends
   - Load balancing strategies (round-robin, least connections, etc.)

2. **Advanced Routing**
   - Path-based routing (not just host-based)
   - Path prefix stripping
   - Request/response header manipulation
   - Custom middleware per route

3. **Observability**
   - Prometheus metrics endpoint
   - Request tracing (OpenTelemetry)
   - Admin dashboard/API
   - Real-time route visualization

4. **Security Features**
   - Rate limiting per route
   - IP whitelisting/blacklisting
   - Request size limits
   - DDoS protection

5. **Certificate Features**
   - DNS-01 challenge for wildcard certificates
   - Support for custom/BYO certificates
   - Multiple certificate authorities
   - Certificate revocation handling

6. **Authentication Enhancements**
   - Multiple OIDC providers simultaneously
   - Path whitelisting (skip auth for specific paths like `/health`)
   - Custom authentication providers
   - JWT validation

7. **Configuration Sources**
   - Configuration files (YAML/JSON)
   - REST API for dynamic configuration
   - Web UI for management

8. **Protocol Support**
   - WebSocket proxying
   - gRPC support
   - TCP/UDP proxying

## Technical Challenges & Solutions

### Challenge 1: YARP Dynamic Configuration
**Problem**: YARP typically uses static configuration  
**Solution**: Implement `IProxyConfigProvider` with `IChangeToken` for hot-reloading

### Challenge 2: Docker Network Discovery
**Problem**: Containers on different Docker networks  
**Solution**: Extract network-specific IP from container inspection

### Challenge 3: Container Lifecycle Management
**Problem**: Containers start/stop at any time  
**Solution**: Docker event stream + startup scan of existing containers

### Challenge 4: Certificate Renewal
**Problem**: Certificates expire and need renewal  
**Solution**: Background service monitors expiration, renews 30 days before expiry

### Challenge 5: Per-Route Authentication
**Problem**: OIDC middleware is global, but auth needed per route  
**Solution**: Custom middleware checks route metadata and enforces auth conditionally

## Contributing

This project is in active development. Contributions are welcome once Phase 5 is complete.

## License

TBD
