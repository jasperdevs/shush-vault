# Shush Vault architecture

Shush Vault is native-first, with one product model and separate OS-native shells.

## Platform shells

| Platform | UI | Status |
| --- | --- | --- |
| Windows | WinUI 3 | runnable on this machine; CI packages with Velopack |
| macOS | SwiftUI | shared-vault format client; macOS CI tests |
| Linux | GTK 4 | shared Rust vault crate client; Linux GTK CI check |

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

The record model is shaped for future sync because each record has a stable ID and timestamps. The Rust CLI, Windows app, macOS app, and Linux app use the same portable encrypted vault envelope: PBKDF2-SHA256 key derivation, AES-256-GCM encryption, base64 salt/nonce/ciphertext, and JSON metadata. The committed vault fixture is tested from Rust, .NET, and Swift so format drift is caught in CI.

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

## Security notes

The vault format supports exactly one KDF profile for version 1: PBKDF2-SHA256 with 310,000 iterations and AES-256-GCM. Readers reject unsupported KDF, cipher, version, and iteration values before deriving keys.

CLI passphrases should be entered through the hidden prompt for normal use. `--passphrase` and `SHUSH_VAULT_PASSPHRASE` exist for automation and are easier to leak through shell history, process metadata, or environment dumps.

Copy and export operations produce plaintext. Clipboard copy paths clear the clipboard after a short TTL when the clipboard still contains the copied value. CLI export requires `--stdout` or `--output` so plaintext output is explicit.
