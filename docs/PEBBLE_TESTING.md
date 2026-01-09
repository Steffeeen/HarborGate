# Testing Harbor Gate with Pebble

Pebble is Let's Encrypt's ACME test server for local development and testing.

## How It Works

Harbor Gate bypasses SSL certificate validation when connecting to Pebble's ACME server by:
1. Setting `SkipAcmeServerCertificateValidation: true` in configuration
2. Using a custom `HttpClient` that accepts Pebble's self-signed certificates
3. Skipping full certificate chain validation when building PFX files

**This SSL bypass only affects Harbor Gate's connection to the ACME server, not the certificates it issues to clients.**

## Prerequisites

- Docker and Docker Compose installed
- Harbor Gate project built

## Quick Start

### 1. Start Pebble and Test Environment

```bash
# From the HarborGate directory
docker-compose -f docker-compose.pebble.yml up -d
```

This will start:
- **Pebble**: ACME server on port 14000 (HTTPS)
- **Harbor Gate**: Reverse proxy with Let's Encrypt/Pebble enabled
- **test-app-1**: Test application at `app1.test.local`
- **test-app-2**: Test application at `app2.test.local`

**Note**: Harbor Gate automatically handles Pebble's self-signed certificates via code-level SSL bypass.

### 2. Test HTTPS Connections

```bash
# Test with curl (use -k to skip client-side verification)
curl -k https://localhost/ -H "Host: app1.test.local"
curl -k https://localhost/ -H "Host: app2.test.local"

# Or add to /etc/hosts for easier testing
echo "127.0.0.1 app1.test.local app2.test.local" | sudo tee -a /etc/hosts
curl -k https://app1.test.local
```

**Note**: The `-k` flag is needed because Pebble issues test certificates that your client doesn't trust. This is expected behavior for testing.

## Monitoring

### Watch Harbor Gate Logs

```bash
docker logs -f harborgate-pebble-test
```

Look for:
- ACME account creation
- Certificate order creation
- HTTP-01 challenge validation
- Certificate download and storage

### Check Pebble Logs

```bash
docker logs -f pebble
```

### Verify Certificate Storage

```bash
# List certificates
ls -la certs-pebble/

# Inspect a certificate (if openssl is installed on host)
openssl pkcs12 -in certs-pebble/localhost.pfx -nokeys -passin pass: | openssl x509 -text -noout
```

## Testing Scenarios

### Scenario 1: Initial Certificate Request

1. Start the environment
2. Make an HTTPS request to trigger certificate issuance
3. Watch logs for ACME flow
4. Verify certificate is issued and stored

### Scenario 2: Certificate Renewal

1. Modify certificate expiration check in code (temporary)
2. Restart Harbor Gate
3. Wait for renewal service to trigger (12 hour interval by default)
4. Verify renewal process

### Scenario 3: Multiple Domains

1. Add more test applications with different hostnames
2. Make requests to each domain
3. Verify separate certificates are issued

### Scenario 4: Challenge Failures

1. Stop Pebble temporarily
2. Make HTTPS request
3. Verify graceful failure handling
4. Restart Pebble
5. Verify retry succeeds

## Configuration

Harbor Gate uses `appsettings.Pebble.json` for Pebble testing:

```json
{
  "HarborGate": {
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "LetsEncrypt": {
        "Email": "test@harborgate.local",
        "AcmeDirectoryUrl": "https://pebble:14000/dir",
        "AcceptTermsOfService": true,
        "SkipAcmeServerCertificateValidation": true
      }
    }
  }
}
```

**Key Configuration Options:**
- `AcmeDirectoryUrl`: Points to Pebble's ACME directory (HTTPS only)
- `SkipAcmeServerCertificateValidation`: **Must be `true`** for Pebble testing
  - Bypasses SSL validation when Harbor Gate connects to Pebble
  - **WARNING**: Never use in production - only for Pebble testing!

## Troubleshooting

### Certificate Request Fails with SSL Error

**Symptom**: `The remote certificate is invalid` or `certificate chain errors`

**Solution**: Ensure `SkipAcmeServerCertificateValidation: true` is set in `appsettings.Pebble.json`

### Certificate Request Fails

**Symptom**: `Failed to request certificate from Let's Encrypt`

**Solutions**:
1. Check Pebble is running: `docker ps | grep pebble`
2. Verify network connectivity between containers
3. Check Harbor Gate logs for detailed errors

### HTTP-01 Challenge Validation Fails

**Symptom**: `HTTP-01 challenge validation failed`

**Solutions**:
1. Verify ACME challenge middleware is registered (check Program.cs)
2. Ensure port 80 is accessible
3. Check that `PEBBLE_VA_ALWAYS_VALID=1` is set in docker-compose (it is by default)

## Cleanup

```bash
# Stop all services
docker-compose -f docker-compose.pebble.yml down

# Remove certificates and volumes
docker-compose -f docker-compose.pebble.yml down -v
rm -rf certs-pebble/
```

## Switching to Production Let's Encrypt

Once testing is complete, update your production configuration:

**For Let's Encrypt Staging (testing with real LE servers):**
```json
{
  "HarborGate": {
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "LetsEncrypt": {
        "Email": "your-email@example.com",
        "UseStaging": true,
        "AcceptTermsOfService": true
        // SkipAcmeServerCertificateValidation: false (or omit - defaults to false)
      }
    }
  }
}
```

**For Let's Encrypt Production:**
```json
{
  "HarborGate": {
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "LetsEncrypt": {
        "Email": "your-email@example.com",
        "UseStaging": false,
        "AcceptTermsOfService": true
        // SkipAcmeServerCertificateValidation: false (or omit - defaults to false)
      }
    }
  }
}
```

**Important Notes:**
- Remove or don't set `AcmeDirectoryUrl` to use Let's Encrypt servers
- **Always set `SkipAcmeServerCertificateValidation: false`** (or omit it) for production
- Let's Encrypt servers have valid trusted certificates - no SSL bypass needed
- Ensure your domain DNS points to your server and ports 80/443 are publicly accessible
