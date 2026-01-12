# Harbor Gate

A reverse proxy built with C# and ASP.NET Core with automatic service discovery via Docker labels, SSL/TLS certificate management, and OpenID Connect authentication.

Built mostly using Claude Sonnet 4.5 with [opencode](https://opencode.ai/).

## Features

- **Dynamic Routing**: Automatic service discovery through Docker labels
- **SSL/TLS**: Let's Encrypt integration with automatic renewal
- **Authentication**: OpenID Connect with role-based access control (RBAC)

## Quick Start

### Using Docker Compose

```yaml
services:
  harborgate:
    image: harborgate:latest
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - ./certs:/var/lib/harborgate/certs
    environment:
      # SSL/TLS Configuration
      - HARBORGATE_ENABLE_HTTPS=true
      - HARBORGATE_ACME_EMAIL=your-email@example.com
      - HARBORGATE_ACME_ACCEPT_TOS=true
      # OpenID Connect Authentication (optional)
      - HARBORGATE_OIDC_ENABLED=true
      - HARBORGATE_OIDC_AUTHORITY=https://auth.example.com
      - HARBORGATE_OIDC_CLIENT_ID=harborgate
      - HARBORGATE_OIDC_CLIENT_SECRET=your-secret
    networks:
      - web

  # Public application (no authentication required)
  frontend:
    image: nginx:alpine
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=app.example.com"
      - "harborgate.tls=true"
    networks:
      - web

  # Protected application (requires authentication and specific role)
  admin:
    image: admin-panel:latest
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=admin.example.com"
      - "harborgate.tls=true"
      - "harborgate.auth.enable=true"
      - "harborgate.auth.roles=admin"
    networks:
      - web

networks:
  web:
    driver: bridge
```

Start the stack:

```bash
docker-compose up -d
```

This example shows:
- **Public route**: `https://app.example.com` (no authentication)
- **Protected route**: `https://api.example.com` (requires `api-user` role)
- **Admin route**: `https://admin.example.com` (requires `admin` role)

## Docker Labels

Configure services using Docker labels:

| Label | Required | Description | Example |
|-------|----------|-------------|---------|
| `harborgate.enable` | Yes | Enable Harbor Gate for this container | `true` |
| `harborgate.host` | Yes | Hostname/domain for routing | `myapp.example.com` |
| `harborgate.port` | No | Target port (auto-discovered if not set) | `8080` |
| `harborgate.tls` | No | Enable TLS for this route | `true` |
| `harborgate.auth.enable` | No | Require authentication | `true` |
| `harborgate.auth.roles` | No | Required roles (comma-separated, OR logic) | `admin,user` |

## Environment Variables

### Core Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `HARBORGATE_ENABLE_HTTPS` | `false` | Enable HTTPS |
| `HARBORGATE_LOG_LEVEL` | `Information` | Log level (Trace, Debug, Information, Warning, Error, Critical) |

### SSL/TLS Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `HARBORGATE_ACME_EMAIL` | - | **Required for Let's Encrypt.** Email for ACME account |
| `HARBORGATE_ACME_ACCEPT_TOS` | `false` | **Required for Let's Encrypt.** Must be `true` to accept Terms of Service |

### OpenID Connect Authentication

| Variable | Default | Description |
|----------|---------|-------------|
| `HARBORGATE_OIDC_ENABLED` | `false` | Enable OIDC authentication |
| `HARBORGATE_OIDC_AUTHORITY` | - | **Required if enabled.** OIDC authority URL (e.g., https://accounts.google.com) |
| `HARBORGATE_OIDC_CLIENT_ID` | - | **Required if enabled.** OAuth 2.0 Client ID |
| `HARBORGATE_OIDC_CLIENT_SECRET` | - | **Required if enabled.** OAuth 2.0 Client Secret |
| `HARBORGATE_OIDC_ROLE_CLAIM_TYPE` | `role` | Claim type for RBAC |

## Development

See [DEVELOPMENT.md](docs/DEVELOPMENT.md) for local development setup, testing, and architecture details.

### Build from Source

```bash
git clone <repository-url>
cd HarborGate
dotnet build
```

### Build Docker Image

```bash
docker build -t harborgate:latest -f src/HarborGate/Dockerfile .
```

### Run Tests

> [!NOTE]
> Running the full test suite takes approximately 20 minutes as it includes comprehensive E2E tests for routing, SSL/TLS, authentication, and WebSockets.

```bash
cd tests
./run-tests.sh all
```

## Technology Stack

- **.NET 10** - Runtime and SDK
- **YARP** - Microsoft's reverse proxy library
- **[Docker.DotNet](https://github.com/dotnet/Docker.DotNet)** - Docker API client
- **[Certes](https://github.com/fszlin/certes)** - ACME/Let's Encrypt client
- **ASP.NET Core Authentication** - OpenID Connect support

## License

MIT License. See [LICENSE](LICENSE) for details.
