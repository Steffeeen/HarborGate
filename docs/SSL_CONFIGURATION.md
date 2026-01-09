# SSL/TLS Configuration Guide

Harbor Gate supports automatic SSL/TLS certificate management with two providers:

1. **Self-Signed Certificates** - For development and testing
2. **Let's Encrypt** - For production with automatic certificate issuance and renewal

## Quick Start

### Development: Self-Signed Certificates

Perfect for local development. Certificates are automatically generated per hostname.

**Configuration** (`appsettings.Development.json`):

```json
{
  "HarborGate": {
    "EnableHttps": true,
    "HttpsPort": 8443,
    "Ssl": {
      "CertificateProvider": "SelfSigned",
      "CertificateStoragePath": "./certs"
    }
  }
}
```

**Usage**:

```bash
# Start Harbor Gate
dotnet run

# Test with curl (use -k to accept self-signed cert)
curl -k --resolve whoami.localhost:8443:127.0.0.1 https://whoami.localhost:8443
```

### Production: Let's Encrypt

Automatic certificate issuance and renewal from Let's Encrypt.

**Configuration** (`appsettings.Production.json`):

```json
{
  "HarborGate": {
    "EnableHttps": true,
    "HttpPort": 80,
    "HttpsPort": 443,
    "Ssl": {
      "CertificateProvider": "LetsEncrypt",
      "CertificateStoragePath": "/var/lib/harborgate/certs",
      "LetsEncrypt": {
        "Email": "your-email@example.com",
        "UseStaging": false,
        "AcceptTermsOfService": true
      }
    }
  }
}
```

**Requirements**:
- Publicly accessible server with ports 80 and 443 open
- Valid DNS records pointing to your server
- Accept Let's Encrypt Terms of Service

## Configuration Options

### Global SSL Settings

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableHttps` | bool | `true` | Enable HTTPS endpoint |
| `HttpsPort` | int | `443` | HTTPS port to listen on |
| `Ssl.CertificateProvider` | string | `"SelfSigned"` | Certificate provider: `"SelfSigned"` or `"LetsEncrypt"` |
| `Ssl.CertificateStoragePath` | string | `/var/lib/harborgate/certs` | Directory to store certificates |

### Self-Signed Provider Options

No additional configuration required. Certificates are:
- Valid for 1 year
- Automatically regenerated when expired
- Stored with hostname as filename (e.g., `example.com.pfx`)
- Include proper Subject Alternative Names (SAN)

### Let's Encrypt Provider Options

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `Email` | string | ✅ Yes | Email for Let's Encrypt account |
| `AcceptTermsOfService` | bool | ✅ Yes | Must be `true` to use Let's Encrypt |
| `UseStaging` | bool | No | Use LE staging environment (for testing) |
| `AcmeDirectoryUrl` | string | No | Custom ACME directory (e.g., for Pebble) |

## Certificate Lifecycle

### Automatic Issuance

Certificates are automatically requested on first HTTPS request for a hostname:

1. Client connects via SNI with hostname
2. Harbor Gate checks for cached certificate
3. If not found, initiates ACME HTTP-01 challenge
4. Let's Encrypt validates domain ownership
5. Certificate is issued and stored
6. Future requests use cached certificate

### Automatic Renewal

The `CertificateRenewalService` runs every 12 hours and:
- Checks all stored certificates
- Renews certificates expiring within 30 days
- Updates cached certificates automatically
- No downtime or manual intervention required

### Certificate Storage

Certificates are stored in PFX format:
- **Location**: Configured via `CertificateStoragePath`
- **Format**: PKCS#12 (.pfx)
- **Filename**: `{hostname}.pfx`
- **Permissions**: Should be readable only by Harbor Gate process

**Example**:
```
/var/lib/harborgate/certs/
├── app1.example.com.pfx
├── app2.example.com.pfx
└── api.example.com.pfx
```

## Testing

### Testing Self-Signed Certificates

```bash
# Generate certificate by making request
curl -k https://test.localhost:8443

# View certificate details
openssl s_client -connect test.localhost:8443 -servername test.localhost < /dev/null 2>&1 | openssl x509 -text

# Check stored certificates
ls -la ./certs/
```

### Testing Let's Encrypt with Pebble

See [PEBBLE_TESTING.md](PEBBLE_TESTING.md) for complete guide to testing ACME flows locally.

Quick start:
```bash
docker-compose -f docker-compose.pebble.yml up -d
curl -k https://app1.test.local
```

### Testing Let's Encrypt Staging

Before using production Let's Encrypt, test with staging:

```json
{
  "Ssl": {
    "CertificateProvider": "LetsEncrypt",
    "LetsEncrypt": {
      "Email": "test@example.com",
      "UseStaging": true,
      "AcceptTermsOfService": true
    }
  }
}
```

Staging has higher rate limits and won't issue trusted certificates (useful for testing).

## Troubleshooting

### Certificate Not Generated

**Symptom**: HTTPS connection fails, no certificate in storage

**Check**:
1. Verify provider is configured correctly
2. Check logs for certificate request errors
3. For Let's Encrypt: Ensure port 80 is accessible for HTTP-01 challenge

```bash
# Check logs
docker logs harborgate | grep -i certificate

# Test HTTP-01 challenge endpoint
curl http://yourdomain.com/.well-known/acme-challenge/test
```

### Let's Encrypt Rate Limits

**Symptom**: `too many certificates already issued for exact set of domains`

**Solutions**:
- Use staging environment for testing
- Wait for rate limit window to reset (1 week)
- See [Let's Encrypt Rate Limits](https://letsencrypt.org/docs/rate-limits/)

### HTTP-01 Challenge Fails

**Symptom**: `HTTP-01 challenge validation failed`

**Common causes**:
1. Port 80 not accessible from internet
2. Firewall blocking HTTP traffic
3. DNS not pointing to correct server
4. Another service bound to port 80

**Solutions**:
```bash
# Test external accessibility
curl http://yourdomain.com/.well-known/acme-challenge/test

# Check port binding
sudo lsof -i :80

# Verify DNS
dig yourdomain.com
```

### Certificate Renewal Fails

**Symptom**: Certificate expires despite renewal service running

**Check**:
1. Renewal service logs
2. Certificate expiration dates
3. Network connectivity to Let's Encrypt

```bash
# Check renewal service logs
docker logs harborgate | grep -i renewal

# View certificate expiration
openssl pkcs12 -in /path/to/cert.pfx -nokeys -passin pass: | openssl x509 -enddate -noout
```

## Security Best Practices

### File Permissions

Restrict certificate directory access:

```bash
# Set restrictive permissions
chmod 700 /var/lib/harborgate/certs
chown harborgate:harborgate /var/lib/harborgate/certs
```

### Docker Deployment

Mount certificate storage as a volume:

```yaml
services:
  harborgate:
    image: harborgate:latest
    volumes:
      - ./certs:/var/lib/harborgate/certs:rw
      - /var/run/docker.sock:/var/run/docker.sock:ro
```

### Email Privacy

Use a dedicated email for Let's Encrypt:
- Receive expiration warnings
- Account recovery
- Security notifications

### Backup Certificates

Regular backups prevent re-issuance:

```bash
# Backup certificates
tar -czf certs-backup-$(date +%Y%m%d).tar.gz /var/lib/harborgate/certs/

# Restore from backup
tar -xzf certs-backup-20260109.tar.gz -C /
```

## Advanced Configuration

### Custom ACME Server

For private ACME infrastructure:

```json
{
  "Ssl": {
    "CertificateProvider": "LetsEncrypt",
    "LetsEncrypt": {
      "Email": "admin@company.com",
      "AcmeDirectoryUrl": "https://acme.company.com/directory",
      "AcceptTermsOfService": true
    }
  }
}
```

### Renewal Interval

Modify renewal check frequency (default: 12 hours):

Edit `Services/CertificateRenewalService.cs`:

```csharp
_checkInterval = TimeSpan.FromHours(6); // Check every 6 hours
```

### Renewal Threshold

Modify renewal timing (default: 30 days before expiration):

Edit provider's `NeedsRenewalAsync` method:

```csharp
var needsRenewal = certInfo.ExpiresWithin(TimeSpan.FromDays(14)); // Renew 14 days before
```

## Migration

### From Self-Signed to Let's Encrypt

1. Update configuration to use Let's Encrypt
2. Restart Harbor Gate
3. Old self-signed certificates will be replaced automatically
4. Clients may see certificate change warnings

### From Other Reverse Proxies

If migrating from Traefik/nginx/Caddy:

1. Note existing domain names
2. Configure Harbor Gate with same domains
3. Update DNS if needed
4. Harbor Gate will request new certificates
5. Old certificates can be safely discarded

## Monitoring

### Certificate Expiration

Monitor certificate expiration dates:

```bash
# List all certificates with expiration
for cert in /var/lib/harborgate/certs/*.pfx; do
  echo "=== $cert ==="
  openssl pkcs12 -in "$cert" -nokeys -passin pass: | openssl x509 -enddate -noout
done
```

### Prometheus Metrics (Future)

Planned metrics:
- `harborgate_certificates_total` - Total certificates managed
- `harborgate_certificates_expiring_soon` - Certificates expiring <30 days
- `harborgate_certificate_renewal_errors_total` - Failed renewal attempts

## FAQ

**Q: Can I use my own certificates?**  
A: Not currently supported. Self-signed or Let's Encrypt only. Manual certificate management is planned for Phase 4.

**Q: Does Harbor Gate support wildcard certificates?**  
A: Not yet. Each hostname gets its own certificate. Wildcard support planned.

**Q: What happens if Let's Encrypt is down?**  
A: Existing certificates continue working. New requests may fail until LE is available. Consider using cached certificates with longer expiration.

**Q: Can I mix self-signed and Let's Encrypt?**  
A: No, the provider is global. Choose one for all domains.

**Q: Are certificates shared between Harbor Gate instances?**  
A: No, each instance manages its own certificates. Use shared storage for HA setups.

## Support

- Documentation: [README.md](README.md)
- Testing Guide: [PEBBLE_TESTING.md](PEBBLE_TESTING.md)
- Issues: [GitHub Issues](https://github.com/your-org/harborgate/issues)
