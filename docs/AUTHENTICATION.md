   # Authentication Guide (Phase 4)

Harbor Gate supports OpenID Connect (OIDC) authentication with role-based access control (RBAC) on a per-route basis.

## Quick Start

### 1. Configure OIDC Provider

Set the following environment variables or update `appsettings.json`:

```bash
HARBORGATE_OIDC_ENABLED=true
HARBORGATE_OIDC_AUTHORITY=https://your-idp.com
HARBORGATE_OIDC_CLIENT_ID=your-client-id
HARBORGATE_OIDC_CLIENT_SECRET=your-client-secret
```

Or in `appsettings.json`:

```json
{
  "HarborGate": {
    "Oidc": {
      "Enabled": true,
      "Authority": "https://your-idp.com",
      "ClientId": "your-client-id",
      "ClientSecret": "your-client-secret",
      "CallbackPath": "/signin-oidc",
      "Scopes": ["openid", "profile", "email"],
      "RoleClaimType": "roles",
      "SaveTokens": false
    }
  }
}
```

### 2. Protect Routes with Authentication

Add authentication labels to your Docker containers:

```yaml
services:
  protected-app:
    image: myapp:latest
    labels:
      - "harborgate.enable=true"
      - "harborgate.host=app.example.com"
      - "harborgate.auth.enable=true"
      - "harborgate.auth.roles=admin,user"  # Optional: require specific roles
```

## Configuration Options

### Global OIDC Settings

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `Enabled` | bool | `false` | Enable OIDC authentication |
| `Authority` | string | - | OIDC provider URL (e.g., `https://accounts.google.com`) |
| `ClientId` | string | - | OAuth 2.0 Client ID |
| `ClientSecret` | string | - | OAuth 2.0 Client Secret |
| `CallbackPath` | string | `/signin-oidc` | Callback path for authentication |
| `Scopes` | string[] | `["openid", "profile", "email"]` | OAuth scopes to request |
| `RoleClaimType` | string | `roles` | Name of the claim containing user roles |
| `SaveTokens` | bool | `false` | Save access/refresh tokens in session |
| `RequireHttpsMetadata` | bool | `true` | Require HTTPS for OIDC metadata (disable only for dev) |

**Environment Variables**:
- `HARBORGATE_OIDC_ENABLED`
- `HARBORGATE_OIDC_AUTHORITY`
- `HARBORGATE_OIDC_CLIENT_ID`
- `HARBORGATE_OIDC_CLIENT_SECRET`
- `HARBORGATE_OIDC_CALLBACK_PATH`
- `HARBORGATE_OIDC_ROLE_CLAIM_TYPE`
- `HARBORGATE_OIDC_SAVE_TOKENS`
- `HARBORGATE_OIDC_REQUIRE_HTTPS_METADATA` (⚠️ set to `false` only for dev/testing)

### Per-Route Authentication Labels

| Label | Required | Description | Example |
|-------|----------|-------------|---------|
| `harborgate.auth.enable` | No | Enable authentication for this route | `true` |
| `harborgate.auth.roles` | No | Required roles (comma-separated, any match grants access) | `admin,user` |

## Authentication Flow

1. **Unauthenticated Request**: User requests a protected route
2. **Redirect to IDP**: Harbor Gate redirects to the OIDC provider
3. **User Login**: User authenticates with the OIDC provider
4. **Callback**: OIDC provider redirects back to Harbor Gate with authorization code
5. **Token Exchange**: Harbor Gate exchanges code for tokens
6. **Session Created**: User session is stored in encrypted cookie
7. **Authorization Check**: Harbor Gate checks user roles against required roles
8. **Access Granted/Denied**: User is either forwarded to backend or receives 403 Forbidden

## OIDC Providers

Harbor Gate supports any standard OpenID Connect provider:

### Keycloak

```json
{
  "Authority": "https://keycloak.example.com/realms/myrealm",
  "ClientId": "harborgate",
  "ClientSecret": "your-secret"
}
```

### Auth0

```json
{
  "Authority": "https://your-tenant.auth0.com",
  "ClientId": "your-client-id",
  "ClientSecret": "your-secret"
}
```

### Azure AD / Microsoft Entra ID

```json
{
  "Authority": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "ClientId": "your-application-id",
  "ClientSecret": "your-secret",
  "Scopes": ["openid", "profile", "email"]
}
```

### Google

```json
{
  "Authority": "https://accounts.google.com",
  "ClientId": "your-client-id.apps.googleusercontent.com",
  "ClientSecret": "your-secret"
}
```

### Okta

```json
{
  "Authority": "https://your-domain.okta.com/oauth2/default",
  "ClientId": "your-client-id",
  "ClientSecret": "your-secret"
}
```

## Role-Based Access Control (RBAC)

### How Roles Work

- Routes can require one or more roles via `harborgate.auth.roles`
- If multiple roles are specified (comma-separated), **any one role grants access**
- If no roles are specified, any authenticated user can access the route
- Roles are read from the JWT token claim specified by `RoleClaimType`

### Example Scenarios

#### Scenario 1: Authentication Only (No Role Required)

```yaml
labels:
  - "harborgate.auth.enable=true"
  # No roles specified - any authenticated user can access
```

#### Scenario 2: Admin Only

```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.auth.roles=admin"
```

#### Scenario 3: Multiple Roles (OR logic)

```yaml
labels:
  - "harborgate.auth.enable=true"
  - "harborgate.auth.roles=admin,editor,viewer"
  # User needs at least ONE of these roles
```

#### Scenario 4: Public Route (No Authentication)

```yaml
labels:
  - "harborgate.enable=true"
  - "harborgate.host=public.example.com"
  # No auth.enable - route is public
```

## Security Considerations

### Cookie Security

- Session cookies are encrypted using ASP.NET Core Data Protection
- Cookies are HTTP-only (not accessible via JavaScript)
- Cookies use `SameSite=Lax` to prevent CSRF attacks
- Cookies are secure when HTTPS is enabled

### Token Storage

- By default, tokens are NOT stored (`SaveTokens: false`)
- Enable `SaveTokens: true` only if you need to access tokens in your backend
- Tokens are encrypted if stored in session

### HTTPS Requirement

- **Always use HTTPS in production** with OIDC authentication
- Most OIDC providers require HTTPS for callbacks
- Session cookies should only be transmitted over HTTPS

### Callback URL Configuration

When configuring your OIDC provider, set the callback/redirect URI to:

```
https://your-harbor-gate-domain/signin-oidc
```

For example:
- `https://gateway.example.com/signin-oidc`

## Troubleshooting

### Authentication Redirect Loop

**Symptom**: Browser keeps redirecting between Harbor Gate and OIDC provider

**Solutions**:
1. Verify callback URL is correctly configured in OIDC provider
2. Check that `CallbackPath` matches your OIDC provider configuration
3. Ensure HTTPS is enabled if required by your OIDC provider
4. Check browser cookies are enabled

### 403 Forbidden After Login

**Symptom**: User authenticates successfully but gets 403 Forbidden

**Solutions**:
1. Check user has required roles in OIDC provider
2. Verify `RoleClaimType` matches the claim name in your JWT token
3. Check Harbor Gate logs for role authorization messages
4. Inspect JWT token to verify roles are present (use jwt.io)

### "Invalid issuer" or "Invalid signature" errors

**Symptom**: Token validation fails with issuer/signature errors

**Solutions**:
1. Verify `Authority` URL is correct and accessible from Harbor Gate
2. Check OIDC provider is using HTTPS
3. Ensure Harbor Gate can reach the OIDC provider's metadata endpoint
4. Check for clock skew between Harbor Gate and OIDC provider

### Missing Roles in Token

**Symptom**: User authenticated but roles claim is missing

**Solutions**:
1. Configure OIDC provider to include roles in ID token or access token
2. Set `GetClaimsFromUserInfoEndpoint: true` in configuration
3. Check `RoleClaimType` matches your provider's role claim name
4. Some providers use different claim names (e.g., `role`, `roles`, `groups`)

## Advanced Configuration

### Custom Role Claim Type

If your OIDC provider uses a different claim name for roles:

```json
{
  "Oidc": {
    "RoleClaimType": "groups"  // or "role", "permissions", etc.
  }
}
```

### Additional Scopes

Request additional scopes from your OIDC provider:

```json
{
  "Oidc": {
    "Scopes": ["openid", "profile", "email", "groups", "custom-scope"]
  }
}
```

### Save Tokens for Backend Access

If your backend needs to access the user's access token:

```json
{
  "Oidc": {
    "SaveTokens": true
  }
}
```

Harbor Gate will forward the authentication cookie to your backend, which can extract tokens if needed.

## Testing

### Test Without OIDC Provider (Development)

For local development without an OIDC provider, you can:

1. Set `Oidc.Enabled: false` to disable authentication
2. Test public routes first
3. Set up a local Keycloak instance for testing

### Test with a Free OIDC Provider

Quick testing options:

1. **Auth0**: Free tier available, easy setup
2. **Keycloak**: Open-source, can run locally with Docker
3. **Google**: Free OAuth 2.0 / OIDC for testing

### Docker Compose Example with Keycloak

```yaml
services:
  keycloak:
    image: quay.io/keycloak/keycloak:latest
    environment:
      - KEYCLOAK_ADMIN=admin
      - KEYCLOAK_ADMIN_PASSWORD=admin
    ports:
      - "8080:8080"
    command: start-dev

  harborgate:
    image: harborgate:latest
    depends_on:
      - keycloak
    environment:
      - HARBORGATE_OIDC_ENABLED=true
      - HARBORGATE_OIDC_AUTHORITY=http://keycloak:8080/realms/master
      - HARBORGATE_OIDC_CLIENT_ID=harborgate
      - HARBORGATE_OIDC_CLIENT_SECRET=secret
```

## Monitoring and Logging

Enable debug logging for authentication issues:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.AspNetCore.Authentication": "Debug",
      "HarborGate.Authorization": "Debug",
      "HarborGate.Middleware": "Debug"
    }
  }
}
```

Look for log messages:
- `"User not authenticated for protected route"` - User needs to login
- `"User lacks required roles"` - Authorization failed
- `"User authorized for host"` - Authorization successful

## Examples

See [docker-compose.example.yml](../docker-compose.example.yml) for complete examples with authentication.

## Support

For issues or questions:
- Check the [Troubleshooting](#troubleshooting) section
- Review logs with debug logging enabled
- Consult your OIDC provider's documentation
- Create an issue on GitHub
