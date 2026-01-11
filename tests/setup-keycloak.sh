#!/bin/bash

# Script to configure Keycloak for Harbor Gate E2E tests
# This creates a realm, client, and test users

set -e

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8090}"
ADMIN_USER="${ADMIN_USER:-admin}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-admin}"

echo "Waiting for Keycloak to be ready..."
for i in {1..60}; do
    if curl -sf "${KEYCLOAK_URL}/health/ready" > /dev/null 2>&1; then
        echo "Keycloak is ready!"
        break
    fi
    echo "Attempt $i/60: Keycloak not ready yet..."
    sleep 2
done

# Get admin token
echo "Getting admin token..."
TOKEN_RESPONSE=$(curl -s -X POST "${KEYCLOAK_URL}/realms/master/protocol/openid-connect/token" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "username=${ADMIN_USER}" \
    -d "password=${ADMIN_PASSWORD}" \
    -d "grant_type=password" \
    -d "client_id=admin-cli")

ACCESS_TOKEN=$(echo $TOKEN_RESPONSE | grep -o '"access_token":"[^"]*' | sed 's/"access_token":"//')

if [ -z "$ACCESS_TOKEN" ]; then
    echo "Failed to get access token"
    echo "Response: $TOKEN_RESPONSE"
    exit 1
fi

echo "Got access token"

# Create realm
echo "Creating harborgate realm..."
curl -s -X POST "${KEYCLOAK_URL}/admin/realms" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{
        "realm": "harborgate",
        "enabled": true,
        "sslRequired": "none",
        "registrationAllowed": false
    }' || echo "Realm might already exist"

# Create client
echo "Creating harborgate-test client..."
curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/clients" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{
        "clientId": "harborgate-test",
        "enabled": true,
        "clientAuthenticatorType": "client-secret",
        "secret": "test-secret-12345",
        "redirectUris": ["*"],
        "webOrigins": ["*"],
        "protocol": "openid-connect",
        "publicClient": false,
        "standardFlowEnabled": true,
        "implicitFlowEnabled": false,
        "directAccessGrantsEnabled": true
    }' || echo "Client might already exist"

# Create realm roles
echo "Creating roles..."
curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/roles" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{"name": "admin"}' || echo "Admin role might already exist"

curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/roles" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{"name": "user"}' || echo "User role might already exist"

# Create test users
echo "Creating test users..."

# Admin user
curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/users" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{
        "username": "admin-user",
        "enabled": true,
        "emailVerified": true,
        "email": "admin@test.local",
        "firstName": "Admin",
        "lastName": "User",
        "credentials": [{
            "type": "password",
            "value": "admin123",
            "temporary": false
        }],
        "requiredActions": []
    }' || echo "Admin user might already exist"

# Regular user
curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/users" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{
        "username": "regular-user",
        "enabled": true,
        "emailVerified": true,
        "email": "user@test.local",
        "firstName": "Regular",
        "lastName": "User",
        "credentials": [{
            "type": "password",
            "value": "user123",
            "temporary": false
        }],
        "requiredActions": []
    }' || echo "Regular user might already exist"

# Get user IDs
echo "Getting user IDs..."
ADMIN_USER_ID=$(curl -s "${KEYCLOAK_URL}/admin/realms/harborgate/users?username=admin-user" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" | grep -o '"id":"[^"]*' | head -1 | sed 's/"id":"//')

REGULAR_USER_ID=$(curl -s "${KEYCLOAK_URL}/admin/realms/harborgate/users?username=regular-user" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" | grep -o '"id":"[^"]*' | head -1 | sed 's/"id":"//')

# Get role IDs
ADMIN_ROLE_ID=$(curl -s "${KEYCLOAK_URL}/admin/realms/harborgate/roles/admin" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" | grep -o '"id":"[^"]*' | head -1 | sed 's/"id":"//')

USER_ROLE_ID=$(curl -s "${KEYCLOAK_URL}/admin/realms/harborgate/roles/user" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" | grep -o '"id":"[^"]*' | head -1 | sed 's/"id":"//')

# Assign roles
echo "Assigning roles..."
if [ -n "$ADMIN_USER_ID" ] && [ -n "$ADMIN_ROLE_ID" ]; then
    curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/users/${ADMIN_USER_ID}/role-mappings/realm" \
        -H "Authorization: Bearer ${ACCESS_TOKEN}" \
        -H "Content-Type: application/json" \
        -d "[{\"id\":\"${ADMIN_ROLE_ID}\",\"name\":\"admin\"}]"
fi

if [ -n "$REGULAR_USER_ID" ] && [ -n "$USER_ROLE_ID" ]; then
    curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/users/${REGULAR_USER_ID}/role-mappings/realm" \
        -H "Authorization: Bearer ${ACCESS_TOKEN}" \
        -H "Content-Type: application/json" \
        -d "[{\"id\":\"${USER_ROLE_ID}\",\"name\":\"user\"}]"
fi

# Configure client to include roles in token
echo "Configuring client mappers..."
curl -s -X POST "${KEYCLOAK_URL}/admin/realms/harborgate/clients/$(curl -s \"${KEYCLOAK_URL}/admin/realms/harborgate/clients?clientId=harborgate-test\" -H \"Authorization: Bearer ${ACCESS_TOKEN}\" | grep -o '\"id\":\"[^\"]*' | head -1 | sed 's/\"id\":\"//')/protocol-mappers/models" \
    -H "Authorization: Bearer ${ACCESS_TOKEN}" \
    -H "Content-Type: application/json" \
    -d '{
        "name": "realm-roles",
        "protocol": "openid-connect",
        "protocolMapper": "oidc-usermodel-realm-role-mapper",
        "config": {
            "claim.name": "roles",
            "jsonType.label": "String",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true",
            "multivalued": "true"
        }
    }' || echo "Mapper might already exist"

echo ""
echo "✓ Keycloak configuration complete!"
echo ""
echo "Realm: harborgate"
echo "Client ID: harborgate-test"
echo "Client Secret: test-secret-12345"
echo ""
echo "Test Users:"
echo "  Admin: admin-user / admin123 (role: admin)"
echo "  User: regular-user / user123 (role: user)"
echo ""
echo "OIDC Endpoints:"
echo "  Authority: ${KEYCLOAK_URL}/realms/harborgate"
echo "  Discovery: ${KEYCLOAK_URL}/realms/harborgate/.well-known/openid-configuration"
