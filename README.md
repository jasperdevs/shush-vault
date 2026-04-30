<div align="center">
  <img src="assets/shush-vault-logo.svg" alt="shush vault logo" width="128" />

  <h1>🤫 shush vault</h1>

  <p><strong>open-source, encrypted secret and API key management for desktop and CLI.</strong></p>

  <p>
    <img alt="GitHub release downloads" src="https://img.shields.io/github/downloads/jasperdevs/shush-vault/total?label=downloads">
    <img alt="GitHub latest release downloads" src="https://img.shields.io/github/downloads/jasperdevs/shush-vault/latest/total?label=downloads%40latest">
    <img alt="GitHub stars" src="https://img.shields.io/github/stars/jasperdevs/shush-vault?style=flat">
  </p>

  <p>
    Shush Vault is a local-first vault for storing, organizing, importing, and copying secrets and API keys from a Windows desktop app and cross-platform CLI.
    The repo is native-first and Windows-first right now: WinUI 3 is the verified desktop app, with SwiftUI and GTK sources kept separate until those clients are wired to the shared encrypted vault and verified on their OSes.
  </p>

  <h2>features</h2>

  <table>
    <tr>
      <th>Feature</th>
      <th>Preview</th>
    </tr>
    <tr>
      <td>Encrypted local vault for API keys, tokens, and secrets</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Encrypted+Vault" alt="Encrypted vault placeholder" width="360"></td>
    </tr>
    <tr>
      <td>Workspaces for projects, clients, and apps</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Workspaces" alt="Workspaces placeholder" width="360"></td>
    </tr>
    <tr>
      <td>Environment labels like Dev, Staging, and Prod</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Environment+Labels" alt="Environment labels placeholder" width="360"></td>
    </tr>
    <tr>
      <td>Search, filters, and masked values by default</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Search+and+Masking" alt="Search and masking placeholder" width="360"></td>
    </tr>
    <tr>
      <td>Add, edit, delete, and copy secrets quickly</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Secret+Editor" alt="Secret editor placeholder" width="360"></td>
    </tr>
    <tr>
      <td>Provider, notes, and metadata for each secret</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=Metadata" alt="Metadata placeholder" width="360"></td>
    </tr>
    <tr>
      <td>.env import with preview, errors, and conflict handling</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=.env+Import" alt=".env import placeholder" width="360"></td>
    </tr>
    <tr>
      <td>CLI for init, add, list, copy, import, and export</td>
      <td><img src="https://placehold.co/640x360/151515/e8e8e8?text=CLI" alt="CLI placeholder" width="360"></td>
    </tr>
  </table>

  <h2>recommended codebase</h2>

  <table>
    <tr>
      <th>Layer</th>
      <th>Choice</th>
      <th>Why</th>
    </tr>
    <tr>
      <td>Desktop shell</td>
      <td>Native per OS</td>
      <td>WinUI 3 for Windows, SwiftUI for macOS, GTK/libadwaita for Linux. No Electron.</td>
    </tr>
    <tr>
      <td>Core logic</td>
      <td>Rust vault core plus matching .NET adapter</td>
      <td>Shared encrypted vault format using PBKDF2-SHA256 and AES-256-GCM so the Windows app and CLI can read the same vault file.</td>
    </tr>
    <tr>
      <td>CLI</td>
      <td>Rust binary with clap</td>
      <td>Fast startup, easy distribution, and direct reuse of the vault crate.</td>
    </tr>
    <tr>
      <td>UI</td>
      <td>OS-native UI</td>
      <td>Each platform uses its own native UI instead of sharing a web shell.</td>
    </tr>
    <tr>
      <td>Storage</td>
      <td>Encrypted local database</td>
      <td>Store searchable metadata separately from encrypted secret values.</td>
    </tr>
    <tr>
      <td>Key handling</td>
      <td>OS keychain plus passphrase fallback</td>
      <td>Use native secure storage where available, while still supporting portable vault unlocks.</td>
    </tr>
  </table>

  <h2>status</h2>

  <table>
    <tr>
      <th>Area</th>
      <th>Status</th>
    </tr>
    <tr>
      <td>Windows app</td>
      <td>WinUI 3 app with custom titlebar, monochrome UI, shared encrypted vault storage, CRUD, search/filter, copy, .env import/export, and Velopack packaging.</td>
    </tr>
    <tr>
      <td>CLI</td>
      <td><code>shush</code> Rust command for init, add, get, update, delete, list/search, copy, import, and export using the shared encrypted vault core.</td>
    </tr>
    <tr>
      <td>macOS</td>
      <td>SwiftUI native source uses the shared encrypted vault format. CI is configured to build it on macOS.</td>
    </tr>
    <tr>
      <td>Linux</td>
      <td>GTK 4 native source uses the shared encrypted vault crate. CI is configured to check it on Linux with GTK 4 dev libraries.</td>
    </tr>
    <tr>
      <td>Sync</td>
      <td>The data model is sync-shaped with stable IDs and timestamps. E2E sync transport is not implemented.</td>
    </tr>
  </table>

  <h2>starter CLI shape</h2>

  <pre><code>$env:SHUSH_VAULT_PASSPHRASE="change-me"
shush init
shush add OPENAI_API_KEY sk-... --workspace my-app --env Dev
shush list --workspace my-app --env Dev --search openai
shush get OPENAI_API_KEY --workspace my-app --env Dev
shush update OPENAI_API_KEY --workspace my-app --env Dev --value sk-new...
shush copy OPENAI_API_KEY --workspace my-app --env Dev
shush import .env --workspace my-app --env Dev --preview
shush import .env --workspace my-app --env Dev --conflict overwrite
shush export --workspace my-app --env Dev
shush delete OPENAI_API_KEY --workspace my-app --env Dev</code></pre>

  <p>MIT</p>
</div>
