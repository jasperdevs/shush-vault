# Shush Vault architecture

Shush Vault is native per platform, with one product model.

## Platform shells

| Platform | UI | Status |
| --- | --- | --- |
| Windows | WinUI 3 | runnable on this machine |
| macOS | SwiftUI | native source present; needs Rust bindings and macOS build verification |
| Linux | GTK 4 / libadwaita | native source present; needs Rust bindings and Linux build verification |

Electron is intentionally not used.

## Shared product model

Every platform works around the same record shape:

- stable `id`
- `workspace`
- `name`
- secret `value`
- `environment`
- `provider`
- `notes`
- `createdAt`
- `updatedAt`

The record model is designed for sync because each record has a stable ID and timestamps. The Rust CLI and Windows app now use the same portable encrypted vault envelope: PBKDF2-SHA256 key derivation, AES-256-GCM encryption, base64 salt/nonce/ciphertext, and JSON metadata.

## Sync plan

Sync should be end-to-end encrypted before any network or cloud layer sees secret values.

The intended shape:

1. Local vault encrypts records before writing them.
2. Sync transport only sees encrypted blobs and metadata needed for conflict resolution.
3. Conflict resolution uses `id` plus `updatedAt`.
4. The app keeps working offline.
5. Cloud sync is optional, not required for local use.

Potential transports:

- GitHub private gist or repo for developer-first sync.
- User-provided WebDAV/S3-compatible storage.
- Shush-hosted relay later, only after the local vault is stable.

## Packaging

Windows packaging uses Velopack. The packaging script builds the app and produces local release artifacts without publishing a GitHub release.

```powershell
.\scripts\package-windows.ps1 -Version 0.1.0
```

GitHub release upload should be added only after the app is ready for public release.
