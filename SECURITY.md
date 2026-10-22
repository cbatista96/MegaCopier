# Security

**English** · [Español](SECURITY.es.md)

## Crash reports and vulnerabilities

Optional reports send unexpected exits and filtered diagnostic data through Cloudflare to a private GitHub repository. Choose in the installer or Settings > Advanced whether to enable them. When automatic reporting is disabled, the unexpected-exit dialog offers a preview and one-time sending.

**Crash reporting is not a security audit.** It does not automatically detect every vulnerability and cannot replace reproduction steps for a security issue.

## Report a vulnerability privately

Do not post credentials, private files or exploitation details in public issues. Use GitHub's private [Report a vulnerability](https://github.com/cbatista96/MegaCopier/security/advisories/new) form. If unavailable, email **contact@worldmarketinglabs.com**. Include the version, reproduction steps and potential impact, with personal data removed. Fixes target the latest maintained version; no specific response deadline is promised.

## Protections and limits

- Server and cloud credentials use Windows Credential Manager.
- Computer-to-computer transfers use the protection of their selected protocol; LocalSend retains its own protocol.
- Mobile control is limited to the local network and protected by a code.
- The forensic signing key is protected with Windows DPAPI for the current user.
- The development version verifies updates using an ECDSA manifest and SHA-256. Windows Authenticode signing remains pending SignPath Foundation approval.
- Automatic update checking contacts GitHub every 48 hours when enabled. Optional automatic reports exclude files, names, paths and free-form messages. The application is not described as making no automatic connections.

See the [privacy policy](docs/Privacy.md), [code-signing policy](docs/Code-signing-policy.md) and [receiver documentation](tools/informes-worker/README.md) (Spanish) for providers and limits.
