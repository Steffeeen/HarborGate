# Harbor Gate E2E Tests

Comprehensive end-to-end tests for Harbor Gate covering routing, SSL certificates, and OIDC authentication.

## Overview

The test suite is organized into multiple test classes, each testing a specific aspect of Harbor Gate:

1. **RoutingTests** - Basic HTTP routing, dynamic container discovery, route updates
2. **SslTests** - SSL/TLS certificates via Pebble ACME server
3. **AuthenticationTests** - OIDC authentication with Keycloak

Each test suite runs in isolation with its own docker-compose environment.

## Prerequisites

- Docker and Docker Compose installed
- .NET 10 SDK installed
- Bash shell (for Keycloak setup script)
- Ports available: 8080, 8090, 8443, 14000

## Quick Start

### Run All Tests

```bash
# From the repository root
cd tests/HarborGate.E2ETests
dotnet test --logger "console;verbosity=detailed"
```

### Run Specific Test Suite

```bash
# Routing tests only
dotnet test --filter "FullyQualifiedName~RoutingTests"

# SSL tests only
dotnet test --filter "FullyQualifiedName~SslTests"

# Authentication tests only
dotnet test --filter "FullyQualifiedName~AuthenticationTests"
```

### Run Individual Test

```bash
dotnet test --filter "FullyQualifiedName~RoutingTests.Test02_App1_BasicRouting_ReturnsWhoamiResponse"
```

## Test Suites

### 1. Routing Tests (`RoutingTests.cs`)

Tests basic reverse proxy functionality and dynamic route management.

**Docker Compose:** `docker-compose.routing.yml`

**Test Scenarios:**
- ✓ Health check endpoint
- ✓ Basic HTTP routing to backend services
- ✓ Explicit port configuration
- ✓ Multi-service routing
- ✓ Unknown host handling (503)
- ✓ Dynamic container addition
- ✓ Container removal and route cleanup

**Run manually:**
```bash
cd tests
docker-compose -f docker-compose.routing.yml up --build
# In another terminal:
curl -H "Host: app1.test.local" http://localhost:8080
```

---

### 2. SSL Tests (`SslTests.cs`)

Tests SSL certificate management using Pebble (Let's Encrypt test server).

**Docker Compose:** `docker-compose.ssl.yml`

**Test Scenarios:**
- ✓ Certificate request from Pebble
- ✓ Certificate caching and reuse
- ✓ Multiple domain certificates
- ✓ Certificate storage persistence
- ✓ HTTP fallback behavior
- ✓ New domain certificate issuance

**Run manually:**
```bash
cd tests
docker-compose -f docker-compose.ssl.yml up --build
# Wait for Pebble to start, then:
curl -k -H "Host: app1.ssl.test" https://localhost:8443
```

**Note:** Uses `-k` flag to accept Pebble's test certificates.

---

### 3. Authentication Tests (`AuthenticationTests.cs`)

Tests OIDC authentication integration with Keycloak.

**Docker Compose:** `docker-compose.auth.yml`

**Setup Script:** `setup-keycloak.sh` (runs automatically during tests)

**Test Scenarios:**
- ✓ Public routes (no auth required)
- ✓ Protected routes (redirect to login)
- ✓ Admin-only routes (RBAC)
- ✓ Keycloak token generation
- ✓ Token validation
- ✓ Invalid credentials rejection
- ✓ Multi-route auth configuration

**Test Users:**
- `admin-user` / `admin123` (role: admin)
- `regular-user` / `user123` (role: user)

**Run manually:**
```bash
cd tests
docker-compose -f docker-compose.auth.yml up --build

# Wait for Keycloak (~30 seconds), then configure it:
./setup-keycloak.sh

# Test public route (should work):
curl -H "Host: public.auth.test" http://localhost:8080

# Test protected route (should redirect to Keycloak):
curl -v -H "Host: protected.auth.test" http://localhost:8080
```

---

## Full Integration Test

For a complete end-to-end test with all features enabled:

**Docker Compose:** `docker-compose.integration.yml`

This environment includes:
- Pebble (ACME server)
- Keycloak (OIDC provider)
- Harbor Gate with SSL + Auth enabled
- Multiple test services with different configurations

```bash
cd tests
docker-compose -f docker-compose.integration.yml up --build

# Configure Keycloak
./setup-keycloak.sh

# Test public HTTP service
curl -H "Host: public.integration.test" http://localhost:8080

# Test HTTPS service (no auth)
curl -k -H "Host: ssl.integration.test" https://localhost:8443

# Test HTTPS + Auth service (will redirect to Keycloak)
curl -v -k -H "Host: secure.integration.test" https://localhost:8443
```

---

## Test Architecture

### Test Lifecycle

Each test class implements `IAsyncLifetime`:

1. **InitializeAsync()** - Starts docker-compose, waits for health checks
2. **Test methods** - Execute actual test scenarios
3. **DisposeAsync()** - Tears down docker-compose environment

### Sequential Execution

Tests are marked with `[Collection("Sequential")]` to prevent port conflicts when running multiple test suites simultaneously.

### Test Isolation

Each test suite uses a unique:
- Docker Compose project name
- Network name
- Container names
- Port mapping (if needed)

---

## Troubleshooting

### Tests Timeout

**Symptom:** Tests fail with "Harbor Gate failed to become healthy"

**Solutions:**
- Increase wait timeout in test code
- Check Docker resources (CPU/memory limits)
- Review Docker logs: `docker logs <container-name>`
- Ensure ports 8080, 8090, 8443, 14000 are available

### Port Already in Use

**Symptom:** Cannot start docker-compose - port conflict

**Solutions:**
```bash
# Check what's using the port
lsof -i :8080

# Kill previous test containers
docker-compose -f docker-compose.routing.yml down
docker-compose -f docker-compose.ssl.yml down
docker-compose -f docker-compose.auth.yml down
```

### Keycloak Setup Fails

**Symptom:** Authentication tests fail during Keycloak configuration

**Solutions:**
- Ensure Keycloak is fully started (wait 30-60 seconds)
- Check Keycloak logs: `docker logs keycloak-test`
- Run setup script manually: `./tests/setup-keycloak.sh`
- Verify curl/jq are installed

### SSL Certificate Errors

**Symptom:** HTTPS requests fail with certificate errors

**Solutions:**
- Ensure Pebble is running: `docker ps | grep pebble`
- Check Pebble logs: `docker logs pebble-test`
- Wait longer for certificate issuance (10-15 seconds)
- Verify ASPNETCORE_ENVIRONMENT=Pebble in Harbor Gate container

---

## Continuous Integration

### GitHub Actions

Example workflow file (`.github/workflows/test.yml`):

```yaml
name: E2E Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    steps:
    - uses: actions/checkout@v3
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: '10.0.x'
    
    - name: Build Harbor Gate
      run: dotnet build src/HarborGate/HarborGate.csproj
    
    - name: Run Routing Tests
      run: dotnet test tests/HarborGate.E2ETests --filter "FullyQualifiedName~RoutingTests" --logger "console;verbosity=detailed"
      timeout-minutes: 10
    
    - name: Run SSL Tests
      run: dotnet test tests/HarborGate.E2ETests --filter "FullyQualifiedName~SslTests" --logger "console;verbosity=detailed"
      timeout-minutes: 15
    
    - name: Run Auth Tests
      run: dotnet test tests/HarborGate.E2ETests --filter "FullyQualifiedName~AuthenticationTests" --logger "console;verbosity=detailed"
      timeout-minutes: 20
    
    - name: Cleanup
      if: always()
      run: |
        cd tests
        docker-compose -f docker-compose.routing.yml down -v || true
        docker-compose -f docker-compose.ssl.yml down -v || true
        docker-compose -f docker-compose.auth.yml down -v || true
```

---

## Development Tips

### Watch Test Logs in Real-Time

```bash
# Terminal 1: Start environment
cd tests
docker-compose -f docker-compose.routing.yml up

# Terminal 2: Watch Harbor Gate logs
docker logs -f harborgate-routing-test

# Terminal 3: Run tests
cd tests/HarborGate.E2ETests
dotnet test --filter "FullyQualifiedName~RoutingTests"
```

### Debug Individual Tests

Add breakpoints in test code and run with debugger:

```bash
# In VS Code or Rider
# Set breakpoint in test method
# Run > Debug Test
```

### Manual Testing

Keep test environment running for manual exploration:

```bash
# Start and leave running
docker-compose -f docker-compose.routing.yml up

# Add custom test containers
docker run -d --name my-test \
  --network harborgate-routing-test_harborgate-test \
  --label "harborgate.enable=true" \
  --label "harborgate.host=mytest.local" \
  traefik/whoami

# Test it
curl -H "Host: mytest.local" http://localhost:8080
```

---

## Adding New Tests

### 1. Create Test Method

```csharp
[Fact]
public async Task Test_MyNewScenario()
{
    // Arrange
    var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
    request.Headers.Host = "my-service.test";

    // Act
    var response = await _httpClient.SendAsync(request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

### 2. Add Backend Service (if needed)

Edit the corresponding `docker-compose.*.yml`:

```yaml
services:
  my-test-service:
    image: my-image
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=my-service.test"
    networks:
      - harborgate-test
```

### 3. Run and Verify

```bash
dotnet test --filter "FullyQualifiedName~MyNewScenario"
```

---

## Test Coverage

Current test coverage:

| Feature | Tests | Status |
|---------|-------|--------|
| Basic Routing | 7 tests | ✅ Complete |
| SSL Certificates | 7 tests | ✅ Complete |
| OIDC Auth | 8 tests | ✅ Complete |
| Dynamic Routes | 2 tests | ✅ Complete |
| Multi-domain SSL | 1 test | ✅ Complete |
| RBAC | 2 tests | ✅ Complete |

---

## Performance Benchmarks

Average test execution times:

- **RoutingTests**: ~2-3 minutes
- **SslTests**: ~3-5 minutes (Pebble startup + cert issuance)
- **AuthenticationTests**: ~5-7 minutes (Keycloak startup)
- **Full Suite**: ~10-15 minutes

Times vary based on:
- Docker performance
- System resources
- Network speed (image pulls)

---

## Contributing

When adding new tests:

1. Follow the existing test naming convention: `Test##_DescriptiveTestName_ExpectedOutcome`
2. Include XML comments explaining what's being tested
3. Use FluentAssertions for readable assertions
4. Clean up resources in `DisposeAsync()`
5. Update this README with new test scenarios
6. Ensure tests can run in isolation and sequentially

---

## Support

For issues or questions:
- Check troubleshooting section above
- Review test output logs
- Check Docker container logs
- Open an issue on GitHub
