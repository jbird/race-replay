# Security policy

## Reporting a vulnerability

Please report vulnerabilities **privately** through GitHub: open the repository's **Security** tab and choose
**Report a vulnerability**
([direct link](https://github.com/jbird/race-replay/security/advisories/new)).
Don't open a public issue.

You can expect an acknowledgement within a week. Fixes ship as a new release; published releases are immutable.

## Supported versions

Only the latest release of the CLI and the current version of the web app on GitHub Pages receive fixes.

## Scope

- **The CLI** (`race-replay`), including how it fetches, caches and writes data.
- **The web app**, including anything that runs in the viewer's browser.
- **Package and bundle parsing.** `.rrz` packages and `.rrb` bundles are untrusted input to the reader: bounds,
  size limits, ZIP entry names and path traversal are all in scope.

Out of scope: the OpenF1 API itself, GitHub's infrastructure, and issues that need a modified CLI or app.

The project uses no secrets. A secret-scanning finding is always a mistake and worth reporting.
