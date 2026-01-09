# Running Harbor Gate Locally for Development

You can run Harbor Gate directly from your IDE or command line without building a Docker container. This is much faster for development and testing.

## Prerequisites

1. **.NET 10 SDK** installed (already have this ✅)
2. **Docker Desktop** running (for Docker socket access)
3. **Test containers** running on the same host

## Quick Start

### Option 1: Run from Command Line

```bash
cd HarborGate/src/HarborGate
dotnet run
```

Harbor Gate will start on port 8080 (configured in `appsettings.Development.json`).

### Option 2: Run from Visual Studio Code

1. Open the `HarborGate` folder in VS Code
2. Press F5 or use Run > Start Debugging
3. Or use the terminal: `dotnet run --project src/HarborGate`

### Option 3: Run from JetBrains Rider / Visual Studio

1. Open `HarborGate.sln`
2. Set `HarborGate` as the startup project
3. Press F5 or click Run

## Setting Up Test Containers

When running Harbor Gate locally (not in a container), you need test containers on the same host:

### Start Test Containers

```bash
# Create a network for testing
docker network create harborgate-local

# Start test container 1
docker run -d \
  --name test-whoami-1 \
  --network harborgate-local \
  -p 8081:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=whoami1.localhost" \
  traefik/whoami

# Start test container 2
docker run -d \
  --name test-whoami-2 \
  --network harborgate-local \
  -p 8082:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=whoami2.localhost" \
  traefik/whoami

# Start test container 3
docker run -d \
  --name test-whoami-3 \
  --network harborgate-local \
  -p 8083:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=whoami3.localhost" \
  traefik/whoami
```

### Test the Routes

Once Harbor Gate is running:

```bash
# Test route 1
curl -H "Host: whoami1.localhost" http://localhost:8080

# Test route 2
curl -H "Host: whoami2.localhost" http://localhost:8080

# Test route 3
curl -H "Host: whoami3.localhost" http://localhost:8080
```

## Configuration for Local Development

The `appsettings.Development.json` is already configured for local development:

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

Key differences from production:
- Runs on port **8080** instead of 80 (no admin privileges needed)
- **Debug** logging level for detailed output
- Same Docker socket access as containerized version

## Environment Variables (Optional)

You can override settings with environment variables:

```bash
# Run with custom settings
HARBORGATE_HTTP_PORT=9000 \
HARBORGATE_LOG_LEVEL=Information \
dotnet run --project src/HarborGate
```

## Testing Dynamic Route Updates

While Harbor Gate is running locally, test dynamic updates:

```bash
# Add a new container dynamically
docker run -d \
  --name dynamic-test \
  --network harborgate-local \
  -p 8084:80 \
  --label "harborgate.enable=true" \
  --label "harborgate.host=dynamic.localhost" \
  traefik/whoami

# Watch Harbor Gate console output - should show:
# "Container started: <id>"
# "Route configured for container dynamic-test: dynamic.localhost -> http://..."

# Test the new route
curl -H "Host: dynamic.localhost" http://localhost:8080

# Stop the container
docker stop dynamic-test

# Watch console - should show:
# "Container stopped/removed: <id>"
# "Route removed: <id>"
```

## Debugging Tips

### Enable Hot Reload (for code changes)

```bash
dotnet watch run --project src/HarborGate
```

This will automatically restart Harbor Gate when you make code changes.

### View Detailed Logs

The console output will show:
- Docker events (container start/stop)
- Port discovery
- Route configuration
- HTTP requests (YARP logs)

Look for logs like:
```
info: HarborGate.Services.DockerMonitorService[0]
      Docker Monitor Service starting
info: HarborGate.Services.DockerMonitorService[0]
      Scanning existing containers
info: HarborGate.Services.RouteConfigurationService[0]
      Route added/updated: abc123 - whoami1.localhost -> http://172.19.0.2:80
```

### Check Route Status

Access the root endpoint to verify Harbor Gate is running:
```bash
curl http://localhost:8080/
```

Response:
```json
{
  "service": "Harbor Gate",
  "status": "running",
  "version": "0.1.0-phases1-2"
}
```

## Common Issues

### Issue: "Permission denied" accessing Docker socket

**Solution for macOS:**
```bash
# Docker Desktop should handle this automatically
# Verify Docker is running:
docker ps

# If that works, Harbor Gate should work too
```

**Solution for Linux:**
```bash
# Add your user to the docker group
sudo usermod -aG docker $USER

# Log out and back in, or:
newgrp docker
```

### Issue: "Cannot connect to Docker daemon"

**Solution:**
1. Start Docker Desktop
2. Verify with: `docker ps`
3. Check socket exists: `ls -la /var/run/docker.sock`

On macOS, the socket at `/var/run/docker.sock` is a symlink to `~/.docker/run/docker.sock`

### Issue: Routes not working

**Problem:** Harbor Gate runs on your Mac, but containers are isolated in Docker networks.

**Solution:** The containers should still be accessible because Docker Desktop bridges networks to the host. However, if you have issues:

1. Check container IP addresses:
```bash
docker inspect test-nginx-1 | grep IPAddress
```

2. Verify you can reach the container from your host:
```bash
# If container IP is 172.19.0.2
curl http://172.19.0.2:80
```

3. If needed, you can use published ports instead of container IPs (requires code modification)

### Issue: Port 8080 already in use

**Solution:** Change the port in `appsettings.Development.json` or use environment variable:
```bash
HARBORGATE_HTTP_PORT=9000 dotnet run --project src/HarborGate
```

## IDE-Specific Setup

### Visual Studio Code

Create `.vscode/launch.json`:
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Harbor Gate (Development)",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/src/HarborGate/bin/Debug/net10.0/HarborGate.dll",
      "args": [],
      "cwd": "${workspaceFolder}/src/HarborGate",
      "stopAtEntry": false,
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "HARBORGATE_LOG_LEVEL": "Debug"
      },
      "console": "internalConsole"
    }
  ]
}
```

Create `.vscode/tasks.json`:
```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/HarborGate.sln",
        "/property:GenerateFullPaths=true",
        "/consoleloggerparameters:NoSummary"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### JetBrains Rider

Rider will automatically detect the project and create run configurations. Just:
1. Open `HarborGate.sln`
2. Right-click on `HarborGate` project > Run
3. Or use the Run button in the toolbar

## Performance Comparison

**Local (Direct Execution):**
- ✅ Instant startup (1-2 seconds)
- ✅ Hot reload support
- ✅ Easy debugging with breakpoints
- ✅ Direct log output to console
- ⚠️ Requires Docker socket access

**Docker Container:**
- ⚠️ Slower startup (build + start)
- ❌ No hot reload (need rebuild)
- ⚠️ Remote debugging required
- ✅ Production-like environment
- ✅ Isolated and portable

For **development**: Use local execution
For **testing full deployment**: Use Docker container

## Development Workflow

1. **Start Harbor Gate locally:**
   ```bash
   cd HarborGate/src/HarborGate
   dotnet watch run
   ```

2. **Start test containers:**
   ```bash
   docker run -d --name test1 --label "harborgate.enable=true" --label "harborgate.host=test.localhost" traefik/whoami
   ```

3. **Make code changes** - Harbor Gate will automatically restart

4. **Test changes:**
   ```bash
   curl -H "Host: test.localhost" http://localhost:8080
   ```

5. **Clean up:**
   ```bash
   docker stop test1 && docker rm test1
   ```

## Next Steps

After verifying local development works:
1. Make code changes
2. Test with `dotnet run`
3. When ready, build Docker image for deployment
4. Test with `docker-compose -f docker-compose.dev.yml up`

This workflow gives you the best of both worlds: fast iteration during development and realistic testing before deployment!
