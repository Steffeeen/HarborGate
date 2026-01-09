# Harbor Gate

A reverse proxy built with C# and .NET, inspired by Traefik. Harbor Gate provides automatic service discovery via Docker labels, SSL certificate management through Let's Encrypt, and OpenID Connect authentication.

Built mostly using Claude Sonnet 4.5 with [opencode](https://opencode.ai/).

## Features

### Phase 1 & 2 (Current)
- ✅ Dynamic reverse proxy using YARP (Yet Another Reverse Proxy)
- ✅ Docker label-based configuration
- ✅ Automatic port discovery
- ✅ Hot-reload routes without restart
- ✅ Real-time Docker container monitoring
- ✅ Automatic SSL/TLS certificates from Let's Encrypt

### Coming Soon
- 🔜 OpenID Connect authentication with RBAC (Phase 4)
- 🔜 Production hardening and monitoring (Phase 5)

## Quick Start

### Option 1: Run Locally (Development)

The fastest way to get started during development:

```bash
# 1. Start test containers
./scripts/start-test-containers.sh

# 2. Run Harbor Gate
cd src/HarborGate
dotnet run

# 3. Test the routes
curl -H "Host: whoami1.localhost" http://localhost:8080
```

See [DEVELOPMENT.md](docs/DEVELOPMENT.md) for detailed local development instructions.

### Option 2: Using Docker Compose

1. Clone the repository
2. Create a `docker-compose.yml` file:

```yaml
services:
  harborgate:
    image: harborgate:latest
    ports:
      - "80:80"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    networks:
      - harborgate

  # Your backend service
  myapp:
    image: traefik/whoami
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=myapp.localhost"
    networks:
      - harborgate

networks:
  harborgate:
    driver: bridge
```

3. Build and run:

```bash
docker-compose up -d
```

4. Access your service at `http://myapp.localhost`

## Configuration

### Docker Labels

Harbor Gate uses Docker labels to configure routing:

| Label | Required | Description | Example |
|-------|----------|-------------|---------|
| `harborgate.enable` | Yes | Enable Harbor Gate for this container | `true` |
| `harborgate.host` | Yes | The hostname/domain for routing | `myapp.example.com` |
| `harborgate.port` | No | Target port (auto-discovered if not set) | `8080` |
| `harborgate.tls` | No | Enable TLS (Phase 3) | `true` |
| `harborgate.auth.enable` | No | Require authentication (Phase 4) | `true` |
| `harborgate.auth.roles` | No | Required roles for RBAC (Phase 4) | `admin,user` |

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `HARBORGATE_DOCKER_SOCKET` | `/var/run/docker.sock` | Path to Docker socket |
| `HARBORGATE_HTTP_PORT` | `80` | HTTP port to listen on |
| `HARBORGATE_HTTPS_PORT` | `443` | HTTPS port (Phase 3) |
| `HARBORGATE_LOG_LEVEL` | `Information` | Logging level |

## How It Works

1. **Container Discovery**: Harbor Gate monitors the Docker socket for container start/stop events
2. **Label Parsing**: Extracts `harborgate.*` labels from running containers
3. **Port Discovery**: Automatically detects exposed ports or uses the specified port
4. **Route Configuration**: Builds YARP routes dynamically: `{host} -> http://{container_ip}:{port}`
5. **Hot Reload**: Updates routes in real-time without restarting

## Architecture

```
┌─────────────────────────────────────┐
│         Harbor Gate                 │
│  ┌──────────────────────────────┐   │
│  │    ASP.NET Core + YARP       │   │
│  │   (Reverse Proxy Engine)     │   │
│  └──────────────────────────────┘   │
│  ┌──────────────────────────────┐   │
│  │  Docker Monitor Service      │   │
│  │  - Watches container events  │   │
│  │  - Parses labels             │   │
│  │  - Discovers ports           │   │
│  │  - Updates routes            │   │
│  └──────────────────────────────┘   │
└─────────────────────────────────────┘
         │                    │
         │ Docker API         │ HTTP
         ▼                    ▼
    Docker Socket       Backend Containers
```

## Development

### Prerequisites
- .NET 10 SDK
- Docker

### Build from source

```bash
# Clone repository
git clone <repository-url>
cd HarborGate

# Build
dotnet build

# Run locally (requires Docker socket access)
dotnet run --project src/HarborGate
```

### Build Docker image

```bash
docker build -t harborgate:latest -f src/HarborGate/Dockerfile .
```

### Development with docker-compose

```bash
# Use the development compose file
docker-compose -f docker-compose.dev.yml up --build

# Test with curl
curl -H "Host: test1.localhost" http://localhost:8080
```

## Examples

### Example 1: Simple Web Application

```yaml
webapp:
  image: mywebapp:latest
  labels:
    - "harborgate.enable=true"
    - "harborgate.host=webapp.example.com"
  networks:
    - harborgate
```

### Example 2: API with Explicit Port

```yaml
api:
  image: myapi:latest
  expose:
    - "8080"
  labels:
    - "harborgate.enable=true"
    - "harborgate.host=api.example.com"
    - "harborgate.port=8080"
  networks:
    - harborgate
```

### Example 3: Multiple Services

```yaml
services:
  harborgate:
    image: harborgate:latest
    ports:
      - "80:80"
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
    networks:
      - web

  frontend:
    image: nginx:alpine
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=app.example.com"
    networks:
      - web

  backend:
    image: myapi:latest
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=api.example.com"
      - "harborgate.port=5000"
    networks:
      - web

  admin:
    image: admin-panel:latest
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=admin.example.com"
    networks:
      - web

networks:
  web:
    driver: bridge
```

## Troubleshooting

### Routes not updating

Check Harbor Gate logs:
```bash
docker logs harborgate
```

Verify Docker socket is mounted:
```bash
docker inspect harborgate | grep docker.sock
```

### Container not accessible

1. Ensure the container is on the same Docker network as Harbor Gate
2. Check that the container has `harborgate.enable=true` label
3. Verify the container is running: `docker ps`
4. Check Harbor Gate logs for errors

### Port discovery issues

If you have multiple exposed ports, explicitly specify the port:
```yaml
labels:
  - "harborgate.port=8080"
```

## Project Roadmap

See [PROJECT_PLAN.md](PROJECT_PLAN.md) for the complete development roadmap including all 5 phases.

## Technology Stack

- **.NET 10** - Runtime and SDK
- **YARP** - Microsoft's reverse proxy library
- **Docker.DotNet** - Docker API client
- **Certes** - ACME/Let's Encrypt client (Phase 3)
- **ASP.NET Core Authentication** - OpenID Connect (Phase 4)

## License

TBD

## Contributing

This project is in active development. Contributions will be welcome once Phase 5 is complete.
