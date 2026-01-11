#!/bin/bash

# Harbor Gate E2E Test Runner
# Runs all E2E test suites with proper cleanup

set -e

COLOR_GREEN='\033[0;32m'
COLOR_RED='\033[0;31m'
COLOR_YELLOW='\033[1;33m'
COLOR_BLUE='\033[0;34m'
COLOR_RESET='\033[0m'

echo -e "${COLOR_BLUE}=== Harbor Gate E2E Test Suite ===${COLOR_RESET}\n"

# Change to tests directory
cd "$(dirname "$0")"

# Function to cleanup Docker environments
cleanup() {
    echo -e "\n${COLOR_YELLOW}Cleaning up Docker environments...${COLOR_RESET}"
    docker-compose -f docker-compose.routing.yml down -v 2>/dev/null || true
    docker-compose -f docker-compose.ssl.yml down -v 2>/dev/null || true
    docker-compose -f docker-compose.auth.yml down -v 2>/dev/null || true
    docker-compose -f docker-compose.integration.yml down -v 2>/dev/null || true
}

# Trap cleanup on exit
trap cleanup EXIT

# Build the test project
echo -e "${COLOR_BLUE}Building test project...${COLOR_RESET}"
cd HarborGate.E2ETests
dotnet build
cd ..

# Test suite selection
TEST_SUITE="${1:-all}"

case $TEST_SUITE in
    routing)
        echo -e "\n${COLOR_GREEN}Running Routing Tests...${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~RoutingTests" --logger "console;verbosity=detailed"
        ;;
    
    ssl)
        echo -e "\n${COLOR_GREEN}Running SSL Tests...${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~SslTests" --logger "console;verbosity=detailed"
        ;;
    
    auth)
        echo -e "\n${COLOR_GREEN}Running Authentication Tests...${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~AuthenticationTests" --logger "console;verbosity=detailed"
        ;;
    
    all)
        echo -e "\n${COLOR_GREEN}Running ALL Tests...${COLOR_RESET}\n"
        
        echo -e "${COLOR_BLUE}1/3: Routing Tests${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~RoutingTests" --logger "console;verbosity=normal"
        cd ..
        cleanup
        sleep 5
        
        echo -e "\n${COLOR_BLUE}2/3: SSL Tests${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~SslTests" --logger "console;verbosity=normal"
        cd ..
        cleanup
        sleep 5
        
        echo -e "\n${COLOR_BLUE}3/3: Authentication Tests${COLOR_RESET}"
        cd HarborGate.E2ETests
        dotnet test --filter "FullyQualifiedName~AuthenticationTests" --logger "console;verbosity=normal"
        cd ..
        ;;
    
    *)
        echo -e "${COLOR_RED}Error: Unknown test suite '$TEST_SUITE'${COLOR_RESET}"
        echo -e "\nUsage: $0 [routing|ssl|auth|all]"
        echo -e "  routing - Run routing tests only"
        echo -e "  ssl     - Run SSL/certificate tests only"
        echo -e "  auth    - Run authentication tests only"
        echo -e "  all     - Run all test suites (default)"
        exit 1
        ;;
esac

echo -e "\n${COLOR_GREEN}✓ Tests complete!${COLOR_RESET}\n"
