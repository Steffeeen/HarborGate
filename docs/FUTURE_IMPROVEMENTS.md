# Future Improvements

This document tracks ideas for enhancing Harbor Gate beyond the core Phase 5 implementation.

## Authentication & Authorization

### OIDC Provider Validation at Startup
**Priority**: High  
**Description**: Validate OIDC provider connectivity and configuration during application startup instead of waiting for the first authentication attempt.

**Implementation Ideas**:
- Fetch and validate `.well-known/openid-configuration` endpoint on startup
- Verify client credentials can authenticate with the provider
- Validate that the `RoleClaimType` exists in the provider's token claims
- Add a startup health check that fails if OIDC is enabled but misconfigured
- Log detailed error messages with troubleshooting steps
- Option to fail fast (exit) or degrade gracefully (disable auth, log warnings)

**Benefits**:
- Catch configuration errors immediately during deployment
- Prevent runtime surprises when users try to authenticate
- Better developer experience with clear error messages

### Custom Access Denied Page
**Priority**: Medium  
**Description**: Replace the default 403 Forbidden response with a user-friendly branded page when users lack required roles.

**Implementation Ideas**:
- Custom middleware to intercept 403 responses
- HTML template with Harbor Gate branding
- Show helpful information:
  - Which service they tried to access
  - What roles are required (if safe to disclose)
  - Who to contact for access (configurable)
  - Link to re-authenticate or logout
- Make page customizable via configuration or HTML file mount
- Support different pages per route via labels (e.g., `harborgate.auth.access-denied-page`)

**Benefits**:
- Professional user experience
- Reduces support requests by providing clear guidance
- Allows branding and customization per deployment

### Path-Based Authentication Exemptions
**Priority**: Medium  
**Description**: Allow certain paths to bypass authentication even when `harborgate.auth.enable=true`.

**Example Use Case**: Protect entire application but allow public access to `/health`, `/api/docs`, `/public/*`

**Label Schema**:
```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.auth.exclude-paths=/health,/api/docs,/public/*"
```

**Benefits**:
- More flexible authentication policies
- Support for health checks on protected services
- Public documentation while protecting the application

### Multiple OIDC Providers
**Priority**: Low  
**Description**: Support multiple OIDC providers simultaneously (e.g., corporate SSO + Google + GitHub).

**Implementation Ideas**:
- Configure multiple named providers
- Select provider per route via label: `harborgate.auth.provider=google`
- Provider selection page if not specified
- Support different role claim types per provider

**Benefits**:
- Support different user populations (employees, customers, partners)
- Allow users to choose their preferred login method

## User Experience

### Custom Error Pages
**Priority**: Medium  
**Description**: Replace all default error pages (404, 500, 502, 503, etc.) with branded, helpful pages.

**Implementation Ideas**:
- Configurable error page templates
- Show context-appropriate information per error type
- 404: "Service not found" - maybe it's not running?
- 502: "Service unavailable" - backend might be starting up
- 503: "Service temporarily unavailable" - try again soon
- Include timestamp, request ID for troubleshooting
- Mount custom HTML files via volume for full customization

### Admin Dashboard / Status Page
**Priority**: Medium  
**Description**: Web UI to view current routes, certificate status, authentication config, and metrics.

**Features**:
- List all active routes discovered from Docker
- Certificate expiration dates and renewal status
- OIDC provider connection status
- Request metrics per route (if metrics added)
- Configuration viewer (redact secrets)
- Optional: route management API for non-Docker deployments

**Security**:
- Protected by authentication (admin role required)
- Read-only view (no modifications via UI)
- Accessible at special hostname like `harborgate.admin.localhost`

## Reliability & Operations

### Startup Configuration Validation
**Priority**: High  
**Description**: Comprehensive validation of all configuration at startup before accepting traffic.

**Validations**:
- Docker socket is accessible and responding
- OIDC provider is reachable and properly configured (if enabled)
- Certificate storage directory is writable
- Port bindings are available (80, 443)
- ACME email is valid format (if Let's Encrypt enabled)
- Log level is valid
- Required environment variables are set

**Benefits**:
- Fail fast with clear error messages
- Prevent partial startup states
- Better CI/CD pipeline integration (healthcheck fails if misconfigured)

### Proactive SSL Certificate Acquisition
**Priority**: High  
**Description**: Request SSL certificates immediately when routes are discovered, rather than waiting for the first HTTPS request to trigger certificate acquisition.

**Current Behavior**: 
- Certificates are requested on-demand when first HTTPS request arrives
- This causes delays for the first user accessing a new service
- Certificate acquisition failures aren't discovered until someone tries to access the service

**Proposed Behavior**:
- When a container with `harborgate.tls=true` is detected, immediately start certificate acquisition
- Process happens in background without blocking route activation
- Log certificate acquisition progress (pending, in-progress, completed, failed)
- Retry failed acquisitions with exponential backoff
- Alert/log if certificate acquisition fails (don't wait for user to discover it)

**Implementation Ideas**:
- Queue certificate requests when routes are discovered
- Background worker processes the queue
- Track certificate state: `pending`, `acquiring`, `ready`, `failed`
- Temporarily serve self-signed cert or redirect to HTTP if acquisition fails
- Show certificate status in admin dashboard (if implemented)
- Option to block route activation until certificate is ready (configurable)

**Benefits**:
- No delay for first user accessing the service
- Discover certificate acquisition problems immediately
- Better user experience (no certificate errors on first access)
- Predictable deployment behavior
- Easier troubleshooting (issues appear in startup logs)

**Configuration**:
```bash
# Wait for certificates before activating routes (optional)
HARBORGATE_CERT_BLOCK_UNTIL_READY=false

# Max time to wait for certificate acquisition during startup
HARBORGATE_CERT_ACQUISITION_TIMEOUT=300s
```

### Graceful Shutdown Improvements
**Priority**: Medium  
**Description**: Enhanced graceful shutdown with connection draining and timeout handling.

**Implementation Ideas**:
- Stop accepting new connections immediately
- Wait for in-flight requests to complete (with timeout)
- Log active connections during shutdown
- Configurable drain timeout (default 30 seconds)
- Send 503 responses to new requests during drain
- Ensure Docker monitor and certificate renewal stop cleanly

### Circuit Breaker for Backend Services
**Priority**: Low  
**Description**: Temporarily stop routing to backends that are consistently failing.

**Implementation Ideas**:
- Track failure rate per backend
- Open circuit after N consecutive failures
- Return 503 immediately while circuit is open
- Periodically probe backend to close circuit
- Log circuit state changes

**Benefits**:
- Faster failure responses (no waiting for backend timeout)
- Reduce load on failing backends
- Better user experience during partial outages

## Security

### Rate Limiting
**Priority**: Medium  
**Description**: Protect backends and the proxy itself from abuse with rate limiting.

**Implementation Ideas**:
- Rate limit per client IP address
- Rate limit per authenticated user
- Rate limit per route (via label: `harborgate.ratelimit=100/minute`)
- Different limits for authenticated vs anonymous users
- Return 429 Too Many Requests with Retry-After header
- Use distributed cache for rate limit state (Redis) in multi-instance setups

### IP Allowlisting/Denylisting
**Priority**: Low  
**Description**: Restrict access to routes based on client IP addresses.

**Label Schema**:
```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.ip.allow=10.0.0.0/8,172.16.0.0/12"  # Corporate networks
  - "harborgate.ip.deny=192.168.1.100"  # Blocked IP
```

**Benefits**:
- Additional security layer beyond authentication
- Restrict admin interfaces to internal networks
- Block known malicious IPs

### Request Size Limits
**Priority**: Low  
**Description**: Limit maximum request body size per route to prevent abuse.

**Label Schema**:
```yaml
labels:
  - "harborgate.request.max-body-size=10MB"
```

### Security Headers
**Priority**: Medium  
**Description**: Automatically add security headers to all responses.

**Headers to Add**:
- `Strict-Transport-Security` (HSTS) for HTTPS routes
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY` (configurable per route)
- `X-XSS-Protection: 1; mode=block`
- `Referrer-Policy: strict-origin-when-cross-origin`
- `Content-Security-Policy` (configurable per route)

**Configuration**:
- Global defaults with per-route overrides via labels
- `harborgate.headers.x-frame-options=SAMEORIGIN`

## Routing & Traffic Management

### Path-Based Routing
**Priority**: High  
**Description**: Route based on URL path in addition to hostname.

**Label Schema**:
```yaml
# Service 1: Handles /api/*
labels:
  - "harborgate.enable=true"
  - "harborgate.host=example.com"
  - "harborgate.path=/api"

# Service 2: Handles /web/*
labels:
  - "harborgate.enable=true"
  - "harborgate.host=example.com"
  - "harborgate.path=/web"
```

**Benefits**:
- Multiple services on same domain
- Microservices architecture support
- API gateway functionality

### Path Prefix Stripping
**Priority**: Medium  
**Description**: Strip path prefix before forwarding to backend.

**Example**: Request to `/api/v1/users` → forward as `/users` to backend

**Label Schema**:
```yaml
labels:
  - "harborgate.path=/api/v1"
  - "harborgate.path.strip-prefix=true"
```

**Benefits**:
- Backends don't need to know their external path prefix
- Easier migration and reorganization of URLs

### Header Manipulation
**Priority**: Medium  
**Description**: Add, modify, or remove headers on requests and responses.

**Label Schema**:
```yaml
labels:
  - "harborgate.headers.request.add=X-Custom-Header:value"
  - "harborgate.headers.request.remove=X-Unwanted-Header"
  - "harborgate.headers.response.add=X-Served-By:HarborGate"
```

**Use Cases**:
- Add correlation IDs for tracing
- Pass authenticated user info to backend
- Remove sensitive headers
- Add caching headers

### Load Balancing (Multiple Backends)
**Priority**: Low  
**Description**: Support multiple backend instances for the same route with load balancing.

**Implementation Ideas**:
- Detect multiple containers with same `harborgate.host` label
- Support load balancing strategies:
  - Round-robin (default)
  - Least connections
  - Random
  - IP hash (sticky sessions)
- Health check backends before routing
- Remove unhealthy backends from pool

**Label Schema**:
```yaml
labels:
  - "harborgate.enable=true"
  - "harborgate.host=api.example.com"
  - "harborgate.loadbalance.strategy=round-robin"
```

### WebSocket Support
**Priority**: Medium  
**Description**: Ensure WebSocket connections are properly proxied.

**Implementation**:
- YARP already supports WebSockets, but validate it works
- Add explicit WebSocket upgrade header handling
- Test with long-lived WebSocket connections
- Document WebSocket usage in examples

### Request/Response Compression
**Priority**: Low  
**Description**: Compress responses to reduce bandwidth.

**Implementation**:
- Enable Brotli and Gzip compression
- Compress by default for text responses
- Configurable per route via label
- Skip compression for already-compressed content (images, video)

## Observability

### Structured Request Logging
**Priority**: High  
**Description**: Log every request with structured data for analysis.

**Log Fields**:
- Timestamp
- Client IP
- HTTP method and path
- Host header
- Route matched
- Backend forwarded to
- Response status code
- Response time (milliseconds)
- Request size
- Response size
- User ID (if authenticated)
- Correlation ID / Request ID
- User agent

**Configuration**:
- Enable/disable per route via label
- Configurable log level (don't log 200 OK, only errors, etc.)
- JSON output for log aggregation systems

### Prometheus Metrics
**Priority**: High  
**Description**: Export metrics for monitoring and alerting.

**Metrics to Track**:
- Request count (by route, status code, method)
- Request duration histogram (by route)
- Request size summary
- Response size summary
- Active connections gauge
- Certificate expiration timestamp (by domain)
- Backend health status (by route)
- OIDC authentication failures count
- Rate limit rejections count

**Endpoint**: `/metrics` (optionally protected by IP allowlist)

### OpenTelemetry Tracing
**Priority**: Medium  
**Description**: Distributed tracing support for requests flowing through Harbor Gate.

**Implementation**:
- Integrate OpenTelemetry SDK
- Create spans for key operations:
  - Request received
  - Authentication check
  - Backend forwarding
  - Response returned
- Propagate trace context to backends
- Export to OTLP collectors (Jaeger, Tempo, etc.)

### Health Check Endpoints
**Priority**: High (Phase 5)**  
**Description**: Endpoints for monitoring Harbor Gate's health.

**Endpoints**:
- `/health` - Basic liveness check (always returns 200 if running)
- `/ready` - Readiness check (validates Docker connection, OIDC provider if enabled)
- `/health/detailed` - Detailed health status (all subsystems)

**Benefits**:
- Kubernetes liveness/readiness probes
- Load balancer health checks
- Monitoring system integration

### Audit Logging
**Priority**: Low  
**Description**: Separate audit log for security-relevant events.

**Events to Audit**:
- Authentication successes and failures
- Authorization denials (403)
- Certificate requests and renewals
- Configuration changes (if management API added)
- Admin dashboard access

**Format**: JSON with timestamp, event type, user, IP, details

## Configuration & Deployment

### Configuration File Support
**Priority**: Low  
**Description**: Support static configuration from YAML/JSON files in addition to Docker labels.

**Use Cases**:
- Non-Docker deployments
- Static routes that don't change
- Complex configurations easier to manage in files
- Mix of file-based and Docker-based routes

### Hot Reload for Configuration Files
**Priority**: Low  
**Description**: Watch configuration files for changes and reload without restart.

### Multi-Architecture Docker Images
**Priority**: Medium  
**Description**: Build Docker images for multiple architectures.

**Architectures**:
- `linux/amd64` (x86_64)
- `linux/arm64` (ARM64/aarch64)
- `linux/arm/v7` (ARM32)

**Benefits**:
- Support Raspberry Pi and ARM servers
- Apple Silicon Macs for local development
- Cloud ARM instances (AWS Graviton, etc.)

### Helm Chart for Kubernetes
**Priority**: Low  
**Description**: Official Helm chart for deploying Harbor Gate in Kubernetes.

**Features**:
- Ingress integration
- ConfigMap for configuration
- Secret management for OIDC credentials
- Horizontal pod autoscaling support
- ServiceMonitor for Prometheus

## Testing

### Integration Test Suite
**Priority**: High (Phase 5)**  
**Description**: Automated tests for key scenarios.

**Test Scenarios**:
- Route discovery from Docker containers
- SSL certificate issuance and renewal
- OIDC authentication flow
- Role-based authorization
- Route hot-reloading
- Graceful shutdown

### Performance Testing
**Priority**: Medium (Phase 5)**  
**Description**: Benchmark Harbor Gate under load.

**Metrics**:
- Requests per second
- Latency percentiles (p50, p95, p99)
- Memory usage over time
- CPU usage under load
- Connection limits

**Tools**: k6, wrk, or Apache Bench

### Chaos Engineering
**Priority**: Low  
**Description**: Test Harbor Gate's resilience to failures.

**Scenarios**:
- Backend container crashes mid-request
- Docker socket becomes unavailable
- OIDC provider goes down
- Network partitions
- Certificate renewal failures

## Documentation

### Video Tutorials
**Priority**: Low  
**Description**: Create video walkthroughs for common scenarios.

**Topics**:
- Getting started in 5 minutes
- Setting up SSL with Let's Encrypt
- Configuring authentication with Keycloak/Authentik
- Troubleshooting common issues

### API Documentation
**Priority**: Low  
**Description**: If management API is added, provide OpenAPI/Swagger docs.

### Architecture Deep Dive
**Priority**: Medium  
**Description**: Detailed documentation of internal architecture for contributors.

**Topics**:
- YARP integration details
- Certificate lifecycle management
- Docker event processing
- Authentication flow diagrams
- Performance considerations

## Advanced Features

### JWT Validation (Without OIDC)
**Priority**: Low  
**Description**: Validate JWT tokens directly without OIDC flow.

**Use Case**: API gateway for services using JWT authentication

**Label Schema**:
```yaml
labels:
  - "harborgate.auth.type=jwt"
  - "harborgate.auth.jwt.issuer=https://auth.example.com"
  - "harborgate.auth.jwt.audience=my-api"
```

### Custom Authentication Providers
**Priority**: Low  
**Description**: Plugin system for custom authentication methods.

**Examples**:
- API keys
- mTLS (client certificates)
- LDAP
- Custom token validation

### TCP/UDP Proxying
**Priority**: Low  
**Description**: Proxy non-HTTP protocols (databases, game servers, etc.).

**Use Cases**:
- PostgreSQL/MySQL proxying with authentication
- Redis proxying
- SSH tunneling
- Game server routing

### Request Transformation
**Priority**: Low  
**Description**: Modify request body before forwarding to backend.

**Use Cases**:
- Add/remove JSON fields
- Convert between data formats
- Inject authentication context into request body

### Response Caching
**Priority**: Low  
**Description**: Cache backend responses to improve performance.

**Implementation**:
- Cache based on HTTP cache headers
- Configurable per route via label
- Distributed cache support (Redis)
- Cache invalidation strategies

---

## Priority Legend

- **High**: Should be implemented soon (Phase 5 or early Phase 6)
- **Medium**: Valuable but not urgent
- **Low**: Nice to have for future consideration

## Contributing Ideas

Have more ideas? Add them to this document or open an issue on GitHub!
