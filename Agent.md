# Harbor Gate Agent Guide

This file gives coding agents the minimum project context needed to make safe,
focused changes. Read the relevant implementation and tests before editing;
documentation and runtime behavior are not always in sync.

## Project Summary

Harbor Gate is a single ASP.NET Core reverse-proxy service targeting .NET 10.
It discovers Docker containers from labels, creates dynamic YARP routes, manages
TLS certificates, and can enforce per-route OpenID Connect authentication and
role-based access control.

There is no database or frontend. Runtime state is primarily held in memory;
certificates can also be persisted to disk.

## Repository Map

- `src/HarborGate/Program.cs`: composition root, Kestrel configuration,
  authentication setup, middleware order, health endpoint, and YARP mapping.
- `src/HarborGate/Configuration/`: strongly typed application options.
- `src/HarborGate/Docker/`: Docker API wrapper and container-label parsing.
- `src/HarborGate/Models/`: container, label, route, and certificate models.
- `src/HarborGate/Services/DockerMonitorService.cs`: initial discovery and
  Docker event handling.
- `src/HarborGate/Services/RouteConfigurationService.cs`: in-memory dynamic
  YARP configuration and change notifications.
- `src/HarborGate/Certificates/`: self-signed and ACME certificate providers,
  plus the HTTP-01 challenge store.
- `src/HarborGate/Services/DynamicCertificateSelector.cs`: SNI certificate
  selection and on-demand acquisition.
- `src/HarborGate/Services/CertificateStorageService.cs`: PFX persistence.
- `src/HarborGate/Services/CertificateRenewalService.cs`: periodic renewal.
- `src/HarborGate/Middleware/`: HTTPS redirect, ACME challenge, and conditional
  authentication middleware.
- `src/HarborGate/Authorization/`: role requirement and authorization handler.
- `tests/HarborGate.E2ETests/`: Docker-backed xUnit E2E suites.
- `tests/docker-compose.*.yml`: suite-specific environments.
- `tests/run-tests.sh`: E2E test orchestrator and cleanup.
- `docs/DEVELOPMENT.md`: development and architecture notes.

## Runtime Flow

The route-discovery path is:

```text
DockerClientWrapper
  -> LabelParser / ContainerInfo
  -> DockerMonitorService
  -> RouteConfigurationService
  -> YARP IProxyConfigProvider
```

Docker labels currently understood by the application are:

- `harborgate.enable`
- `harborgate.host`
- `harborgate.port`
- `harborgate.tls`
- `harborgate.auth.enable`
- `harborgate.auth.roles`

The request pipeline is configured in `Program.cs`. Middleware order matters,
especially for rate limiting, HTTPS redirects, ACME HTTP-01 challenges,
authentication, `/_health`, and `MapReverseProxy()`.

Backends are proxied over HTTP. Harbor Gate terminates client TLS at Kestrel.
When running in Docker, backend addressing uses a discovered container IP and
internal port. When running on the host, it uses `127.0.0.1` and the backend
port must be published.

## Build and Run

Requirements:

- .NET 10 SDK
- Docker and Docker Compose for E2E tests and realistic local operation
- Access to a Docker daemon/socket for container discovery

Common commands, run from the repository root unless noted:

```bash
dotnet build HarborGate.slnx
dotnet run --project src/HarborGate
dotnet watch run --project src/HarborGate
docker build -t harborgate:latest -f src/HarborGate/Dockerfile .
```

Building and testing creates `bin/` and `obj/` output.

## Tests

Tests are E2E tests, not isolated unit tests. They create Docker Compose
environments and can be slow. The full suite is documented as taking about 20
minutes.

Run a focused suite when possible:

```bash
./tests/run-tests.sh routing
./tests/run-tests.sh ssl
./tests/run-tests.sh auth
./tests/run-tests.sh websocket
./tests/run-tests.sh all
```

The suite names map to `RoutingTests`, `SslTests`, `AuthenticationTests`, and
`WebSocketTests`. Tests are intended to run sequentially because they bind
fixed ports and manage shared Docker resources.

For changes not requiring Docker, at minimum run:

```bash
dotnet build HarborGate.slnx
```

There is no dedicated lint command or unit-test project. If adding logic that
can be tested without Docker, consider a focused unit test only when it adds
clear value; do not unnecessarily expand the project structure for a small
change.

## Configuration

Defaults and environment-specific settings are in:

- `src/HarborGate/appsettings.json`
- `src/HarborGate/appsettings.Development.json`
- `src/HarborGate/appsettings.Testing.json`
- `src/HarborGate/appsettings.Pebble.json`
- `src/HarborGate/appsettings.SslTesting.json`

Options are defined in
`src/HarborGate/Configuration/HarborGateOptions.cs` and bound in `Program.cs`.
`Program.cs` also manually applies selected flat `HARBORGATE_*` environment
variables.

Do not assume every environment variable mentioned in documentation is wired
to runtime behavior. Confirm both option binding and any explicit override in
`Program.cs`. Standard ASP.NET hierarchical variables use double underscores,
for example `HarborGate__Oidc__Enabled`.

Never commit real OIDC secrets, ACME account material, certificates, or other
local credentials.

## Change Guidelines

- Prefer the smallest correct change and preserve existing ASP.NET Core/YARP
  patterns.
- Keep nullable-reference-type warnings clean.
- Use async APIs end to end where practical; pay special attention to Docker
  event callbacks and TLS code where sync-over-async can block or hide errors.
- Treat route configuration as concurrent state. Docker events may arrive
  close together, and YARP observes updates through change tokens.
- Preserve middleware ordering unless the change explicitly requires new
  request semantics.
- Include enough structured logging to diagnose lifecycle failures, but do not
  log tokens, secrets, cookies, or certificate private material.
- Avoid broad exception swallowing. If continued operation is intentional,
  log actionable context and preserve cancellation behavior.
- For Docker behavior, test containers with multiple networks and multiple
  exposed ports when relevant.
- For TLS behavior, distinguish HTTP `Host` from TLS SNI; changing a Host
  header alone does not test certificate selection for that hostname.
- Update documentation when labels, environment variables, commands, or
  observable behavior change.
- Do not modify unrelated worktree changes or generated `bin/`/`obj/` files.

## Known Debugging Hotspots

These are investigation pointers, not requirements to redesign the system in
unrelated changes:

- Certificate acquisition can occur during Kestrel's SNI callback. Inspect for
  blocking, first-request timeouts, and duplicate concurrent issuance.
- Certificate renewal must not discard a still-valid certificate before a
  replacement is successfully acquired and persisted.
- Docker network selection must choose an address reachable from Harbor Gate;
  a container's first listed network is not necessarily suitable.
- Docker event processing and route change-token replacement are concurrency
  sensitive. Async callbacks must surface exceptions and preserve ordering
  where required.
- Multiple containers declaring the same hostname can produce competing YARP
  routes rather than a single load-balanced cluster.
- `harborgate.tls` is represented in route models, but verify every consumer
  before assuming it controls redirects or certificate issuance.
- Authentication depends on proxy scheme/host correctness. When debugging OIDC
  callbacks behind another proxy, inspect forwarded-header handling, callback
  URLs, cookie security, and Data Protection key persistence.
- `/_health` currently indicates process availability more than dependency or
  route readiness.
- Rate-limiter registration does not by itself prove that a policy is attached
  to proxy endpoints.
- E2E assertions have uneven depth. Read each test to confirm it exercises the
  intended layer rather than only an external dependency.

## Before Finishing a Change

1. Re-read the modified request or lifecycle path for cancellation,
   concurrency, and failure behavior.
2. Run `dotnet build HarborGate.slnx`.
3. Run the narrowest relevant E2E suite when Docker is available.
4. Inspect the diff for unrelated changes, generated files, and secrets.
5. Report exactly what was verified and any tests that were not run.
