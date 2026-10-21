# Code signing policy

## Current status

MegaCopier is preparing an application to SignPath Foundation. Sponsorship and certificate access have **not** been approved. Current public executables and installers are not represented as Authenticode-signed.

If the application is approved, the attribution will be: **Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).** This sentence describes the intended provider; it is not a claim that sponsorship has already been granted.

## Team and approval

- Maintainer, committer and reviewer: [cbatista96](https://github.com/cbatista96).
- Intended signing approver: [cbatista96](https://github.com/cbatista96), subject to the account and role configuration accepted by SignPath.
- Outside contributions must be reviewed before inclusion in a signed release. Each signing request requires explicit manual approval. All source and signing account holders must use MFA; this policy does not claim that SignPath accounts have already been created or configured.

## What may be signed

Only MegaCopier-owned binaries built from this repository, and its installer, may be submitted under the project's signing policy. Microsoft and other upstream binaries must retain their original signatures and must not be re-signed with the project's certificate. Builds submitted to SignPath must originate from GitHub-hosted Actions runners and be verifiable against their source and workflow.

Application, CLI and shell-extension binaries must be signed before building the final installer and portable archive. The installer must then be signed. SHA-256 sums and the ECDSA update manifest must be generated from the final signed packages; changing any package afterwards invalidates those sums and the update signature. Any signing or verification failure must stop publication.

The ECDSA update key is separate from Authenticode. Its public half is included in the application; its private half is stored outside the repository and in a GitHub Actions secret. Neither private key nor signing-service tokens may be distributed in the app or repository.

## Privacy and user choices

See the [privacy policy](Privacy.md) and [Spanish version](Privacidad.md). Update checks and optional error reporting have installer and Settings controls. The privacy policy is displayed before installation. Reporting must be optional, and copied files must not be attached automatically to reports. The policy describes GitHub and Cloudflare processing and report retention.

Signing identifies the publisher and supports integrity checks. It does not guarantee that every Windows SmartScreen warning will disappear.
