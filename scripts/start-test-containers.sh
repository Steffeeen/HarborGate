#!/bin/bash

# Harbor Gate - Local Development Test Container Setup
# This script sets up test containers for local Harbor Gate development

set -e

echo "🚀 Setting up Harbor Gate test containers for local development..."

# Create network if it doesn't exist
if ! docker network inspect harborgate-local &> /dev/null; then
    echo "📡 Creating harborgate-local network..."
    docker network create harborgate-local
else
    echo "✓ Network harborgate-local already exists"
fi

# Function to start a container
start_container() {
    local name=$1
    local port=$2
    local host=$3
    local image=$4
    
    if docker ps -a --format '{{.Names}}' | grep -q "^${name}$"; then
        echo "✓ Container $name already exists, restarting..."
        docker start $name
    else
        echo "🐳 Starting container: $name on port $port"
        docker run -d \
            --name $name \
            --network harborgate-local \
            -p $port:80 \
            --label "harborgate.enable=true" \
            --label "harborgate.host=$host" \
            $image
    fi
}

# Start test containers with Traefik whoami image
start_container "test-whoami-1" "8081" "whoami1.localhost" "traefik/whoami"
start_container "test-whoami-2" "8082" "whoami2.localhost" "traefik/whoami"
start_container "test-whoami-3" "8083" "whoami3.localhost" "traefik/whoami"

echo ""
echo "✅ Test containers are ready!"
echo ""
echo "📋 Container Summary:"
echo "  • test-whoami-1 → http://whoami1.localhost:8080 (direct: http://localhost:8081)"
echo "  • test-whoami-2 → http://whoami2.localhost:8080 (direct: http://localhost:8082)"
echo "  • test-whoami-3 → http://whoami3.localhost:8080 (direct: http://localhost:8083)"
echo ""
echo "🎯 Next steps:"
echo "  1. Start Harbor Gate:"
echo "     cd src/HarborGate && dotnet run"
echo ""
echo "  2. Test the routes (in another terminal):"
echo "     curl -H 'Host: whoami1.localhost' http://localhost:8080"
echo "     curl -H 'Host: whoami2.localhost' http://localhost:8080"
echo "     curl -H 'Host: whoami3.localhost' http://localhost:8080"
echo ""
echo "  3. Stop containers when done:"
echo "     ./scripts/stop-test-containers.sh"
echo ""
