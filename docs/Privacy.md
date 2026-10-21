# Privacy policy

Last updated: October 21, 2026. Project: [MegaCopier](https://github.com/cbatista96/MegaCopier), maintained by [cbatista96](https://github.com/cbatista96). Contact: contact@worldmarketinglabs.com.

## Files and local data

File transfers take place between the locations and services selected by the user. MegaCopier does not send copied files to the maintainer. Settings, transfer history, pending jobs and logs are stored locally in `%LOCALAPPDATA%\MegaCopier`, or in `Data` next to the portable app. Local logs can contain file names and paths; they are not attached automatically to error reports.

## Update checks and release notes

Automatic update checking is selected by default in the installer and can be disabled there or in Settings. It checks GitHub after 48 hours have elapsed since the last successful check; an overdue check can run when Internet access returns. Users can also request a manual check. GitHub receives the connection's IP address and a User-Agent containing the app version. Checking does not send file names, transfer history or file content.

Downloads and remote release-note images use GitHub's services. The app caches the last result and notes locally. Automatic downloading and installation are off by default and have separate controls. Downloads respect the configured network permissions.

## Optional error reports

The interactive installer offers **Send error reports**, selected by default; users can deselect it before installation. This preference can also be changed in Settings. When enabled, authorized reports are sent in the background. When disabled, users can inspect a report and choose whether to send it after an unexpected exit; choosing not to send does not transmit that report. Selecting **Always send without asking** enables automatic reporting. Disabling reporting clears its pending upload queue.

Reports contain a random identifier for that report, creation time, app and Windows versions, language, app mode, theme, a general activity category, filtered exception types and function names, and, when available, the failing native module, error code and offset. A hash groups repeated failures. The report excludes file contents, user file names and paths, free-form exception messages and a permanent device identifier. An unexpected-exit report may also follow a power interruption. The in-app preview shows the JSON to be sent.

Reports are sent over HTTPS through a Cloudflare Worker to the maintainer's **private** GitHub reports repository. Cloudflare necessarily receives the connection's IP address; the Worker derives an HMAC value for abuse limiting and does not put the IP address into the report or GitHub issue. Reports are available to the maintainer and to people the maintainer explicitly authorizes for that private repository. The app does not contain the repository access token.

The app keeps at most 20 pending authorized reports, discarding reports older than 14 days when processing the queue, and keeps delivered-report fingerprints for 90 days. The receiver keeps delivery receipts for 14 days, quota records for 30 days, and grouping records until removal by the maintainer. Reports stored as private GitHub issues currently have **no automatic deletion period**. They remain until removed by the maintainer. To request deletion or ask about a report, contact contact@worldmarketinglabs.com and, if available, provide its report identifier. Do not email private files or unfiltered logs unless separately agreed.

## Other network features

Network copying, cloud services, remote control and device discovery depend on the features and destinations the user enables or selects. Data required for those transfers is sent to those destinations. Opening a link or manually submitting a public GitHub issue uses the browser and the services selected by the user; information submitted in a public issue is public.

## Service providers and changes

GitHub and Cloudflare process connection and service data under their own policies: [GitHub privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) and [Cloudflare privacy policy](https://www.cloudflare.com/privacypolicy/). Their processing is distinct from the filtered content of a MegaCopier error report.

Future changes to data collection or transmission must be reflected in this policy and the installer and Settings descriptions. The Spanish version is in [Privacidad.md](Privacidad.md).
