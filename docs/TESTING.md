# Testing Harbor Gate (Phases 1 & 2)

## Prerequisites

1. Docker Desktop installed and running
2. .NET 10 SDK installed

## Building

### Build .NET Project
```bash
cd HarborGate
dotnet build
```

### Build Docker Image
```bash
docker build -t harborgate:latest -f src/HarborGate/Dockerfile .
```

## Testing with Docker Compose

### Option 1: Development Setup

1. Start the development environment:
```bash
docker-compose -f docker-compose.dev.yml up --build
```

2. This will start:
   - Harbor Gate on port 8080
   - Three test services (traefik/whoami containers)
   - All on the `harborgate-dev` network

3. Test the routes:
```bash
# Test service 1
curl -H "Host: whoami1.localhost" http://localhost:8080

# Test service 2
curl -H "Host: whoami2.localhost" http://localhost:8080

# Test service 3
curl -H "Host: whoami3.localhost" http://localhost:8080
```

4. View logs:
```bash
# Harbor Gate logs
docker logs harborgate-dev -f

# Should show:
# - Docker Monitor Service starting
# - Scanning existing containers
# - Routes configured for each test service
```

### Option 2: Production-like Setup

1. Start the example environment:
```bash
docker-compose -f docker-compose.example.yml up --build
```

2. This will start:
   - Harbor Gate on port 80
   - Two example services (traefik/whoami)

3. Test the routes:
```bash
# Test whoami1
curl -H "Host: whoami1.localhost" http://localhost

# Test whoami2
curl -H "Host: whoami2.localhost" http://localhost
```

## Manual Testing Steps

### Test 1: Basic Routing

1. Start Harbor Gate and a test container:
```bash
docker-compose -f docker-compose.dev.yml up -d
```

2. Verify routes are discovered:
```bash
docker logs harborgate-dev
```

Expected output:
```
Docker Monitor Service starting
Scanning existing containers
Found 3 containers with Harbor Gate configuration
Route configured for container test-whoami-1: whoami1.localhost -> http://172.x.x.x:80
Route configured for container test-whoami-2: whoami2.localhost -> http://172.x.x.x:80
Route configured for container test-whoami-3: whoami3.localhost -> http://172.x.x.x:80
```

3. Test HTTP requests:
```bash
curl -v -H "Host: whoami1.localhost" http://localhost:8080
```

Expected: whoami response showing request details (200 OK)

### Test 2: Dynamic Route Updates

1. Start Harbor Gate:
```bash
docker-compose -f docker-compose.dev.yml up -d harborgate-dev
```

2. Add a new container dynamically:
```bash
docker run -d --name dynamic-test \
  --network harborgate-dev \
  --label "harborgate.enable=true" \
  --label "harborgate.host=dynamic.localhost" \
  traefik/whoami
```

3. Check Harbor Gate logs:
```bash
docker logs harborgate-dev
```

Expected output:
```
Container started: <container-id>
Route configured for container dynamic-test: dynamic.localhost -> http://172.x.x.x:80
```

4. Test the new route:
```bash
curl -H "Host: dynamic.localhost" http://localhost:8080
```

Expected: whoami response

5. Remove the container:
```bash
docker stop dynamic-test
docker rm dynamic-test
```

6. Check logs again:
```bash
docker logs harborgate-dev
```

Expected:
```
Container stopped/removed: <container-id>
Route removed: <container-id>
```

### Test 3: Port Discovery

1. Test automatic port discovery:
```bash
docker run -d --name port-test \
  --network harborgate-dev \
  --label "harborgate.enable=true" \
  --label "harborgate.host=porttest.localhost" \
  traefik/whoami
```

2. Check logs for auto-discovered port:
```bash
docker logs harborgate-dev | grep "Auto-discovered"
```

Expected:
```
Auto-discovered port 80 for container <container-id>
```

### Test 4: Explicit Port Configuration

1. Start a container with explicit port:
```bash
docker run -d --name explicit-port \
  --network harborgate-dev \
  --label "harborgate.enable=true" \
  --label "harborgate.host=explicit.localhost" \
  --label "harborgate.port=80" \
  traefik/whoami

```

2. Check logs:
```bash
docker logs harborgate-dev | grep "explicit-port"
```

Expected:
```
Route configured for container explicit-port: explicit.localhost -> http://172.x.x.x:80
```

### Test 5: Multiple Containers

1. Start multiple backend services:
```bash
docker-compose -f docker-compose.dev.yml up -d
```

2. Test all routes:
```bash
# Test each service
for host in whoami1 whoami2 whoami3; do
  echo "Testing $host.localhost..."
  curl -s -H "Host: $host.localhost" http://localhost:8080 | head -n 5
  echo "---"
done
```

3. Verify all routes work simultaneously

### Test 6: Container Restart

1. Restart a backend container:
```bash
docker restart test-whoami-1
```

2. Monitor Harbor Gate logs:
```bash
docker logs harborgate-dev -f
```

Expected:
```
Container stopped/removed: <container-id>
Route removed: <container-id>
Container started: <container-id>
Route configured for container test-whoami-1: whoami1.localhost -> http://172.x.x.x:80
```

3. Test route still works:
```bash
curl -H "Host: whoami1.localhost" http://localhost:8080
```

## Verification Checklist

- [ ] Harbor Gate builds successfully with `dotnet build`
- [ ] Docker image builds successfully
- [ ] Docker Compose starts without errors
- [ ] Harbor Gate connects to Docker socket
- [ ] Existing containers are scanned on startup
- [ ] Routes are created for labeled containers
- [ ] HTTP requests are proxied correctly
- [ ] New containers are detected and routed
- [ ] Stopped containers have routes removed
- [ ] Container restarts update routes properly
- [ ] Port auto-discovery works
- [ ] Explicit port labels work
- [ ] Multiple containers work simultaneously
- [ ] Logs show clear information about route changes

## Common Issues

### Docker socket permission denied
If you see "permission denied" accessing /var/run/docker.sock:
```bash
# On Linux, add user to docker group
sudo usermod -aG docker $USER
```

### Container not detected
Verify:
1. Container has `harborgate.enable=true` label
2. Container has `harborgate.host` label
3. Container is on the same Docker network as Harbor Gate

### Route not working
Check:
1. Harbor Gate logs for errors
2. Container IP address is accessible from Harbor Gate
3. Target port is correct (check logs for port discovery)
4. Host header matches the configured hostname

## Next Steps

After verifying Phases 1 & 2 work correctly:
- Phase 3: Implement Let's Encrypt SSL/TLS
- Phase 4: Add OpenID Connect authentication
- Phase 5: Production hardening and monitoring

## Troubleshooting Commands

```bash
# View Harbor Gate logs
docker logs harborgate-dev -f

# Inspect Harbor Gate container
docker inspect harborgate-dev

# Check Docker network
docker network inspect harborgate-dev

# List all containers with Harbor Gate labels
docker ps --filter label=harborgate.enable=true

# Check if container is accessible from Harbor Gate
docker exec harborgate-dev ping -c 1 <container-ip>

# View YARP routing info (if debugging needed)
docker logs harborgate-dev | grep -i yarp
```
