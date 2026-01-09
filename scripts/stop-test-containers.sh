#!/bin/bash

# Harbor Gate - Stop and cleanup test containers

echo "🛑 Stopping Harbor Gate test containers..."

# Stop and remove containers
docker stop test-whoami-1 test-whoami-2 test-whoami-3 2>/dev/null || true
docker rm test-whoami-1 test-whoami-2 test-whoami-3 2>/dev/null || true

echo "✅ Test containers stopped and removed"
echo ""
echo "To remove the network (optional):"
echo "  docker network rm harborgate-local"
