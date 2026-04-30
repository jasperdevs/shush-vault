<p align="center">
  <img src="assets/logo.png" alt="API Vault logo" width="128" />
</p>

# API Vault

<p>
  <img alt="GitHub release downloads" src="https://img.shields.io/github/downloads/jasperdevs/api-vault/total?label=downloads">
  <img alt="GitHub latest release downloads" src="https://img.shields.io/github/downloads/jasperdevs/api-vault/latest/total?label=downloads%40latest">
  <img alt="GitHub stars" src="https://img.shields.io/github/stars/jasperdevs/api-vault?style=flat">
</p>

Open-source, cross-platform API key management for desktop and CLI.

API Vault is a local-first vault for storing, organizing, importing, and copying API keys across macOS, Windows, and Linux. The goal is a simple desktop app plus a scriptable CLI, with encrypted storage and no required cloud account.

## Planned Features

| Feature | Preview |
| --- | --- |
| Local encrypted vault for API keys, tokens, and secrets | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Encrypted+Vault" alt="Encrypted vault placeholder" width="360"> |
| Workspaces for separating projects, clients, or apps | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Workspaces" alt="Workspaces placeholder" width="360"> |
| Environment labels like Dev, Staging, and Prod | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Environment+Labels" alt="Environment labels placeholder" width="360"> |
| Quick search, filtering, and clean table browsing | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Search+and+Filter" alt="Search and filter placeholder" width="360"> |
| One-click copy with masked values by default | <img src="https://placehold.co/640x360/151515/e8e8e8?text=One-click+Copy" alt="One-click copy placeholder" width="360"> |
| Create, edit, update, and delete secrets in one place | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Manage+Secrets" alt="Manage secrets placeholder" width="360"> |
| Import existing `.env` files with preview and conflict handling | <img src="https://placehold.co/640x360/151515/e8e8e8?text=.env+Import" alt=".env import placeholder" width="360"> |
| Optional provider, notes, and metadata for each secret | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Secret+Metadata" alt="Secret metadata placeholder" width="360"> |
| Cross-platform desktop app for macOS, Windows, and Linux | <img src="https://placehold.co/640x360/151515/e8e8e8?text=Desktop+App" alt="Desktop app placeholder" width="360"> |
| CLI for adding, listing, importing, exporting, copying, and scripting secrets | <img src="https://placehold.co/640x360/151515/e8e8e8?text=CLI" alt="CLI placeholder" width="360"> |

## Feature Scope

API Vault should cover the visible Isla-style feature set without cloning the product blindly:

- Local-first storage with no required cloud sync
- Secure encrypted vault unlock flow
- Workspace creation and switching
- Secret creation form
- Secret list/table view
- Masked secret values
- Search secrets
- Environment filters and multi-environment tagging
- Provider field
- Notes field
- Copy secret value
- Edit and update secrets
- Delete secrets
- Import `.env` files
- Import preview before saving
- Import conflict strategy such as skip, overwrite, or rename
- Import status and error reporting
- Cross-platform packaging for macOS, Windows, and Linux
- CLI commands for everyday workflows
- Open-source license and public development

## CLI Goals

```bash
apivault init
apivault add OPENAI_API_KEY
apivault list --workspace my-app
apivault copy OPENAI_API_KEY
apivault import .env --workspace my-app --env dev
apivault export --format env
```

## License

MIT
