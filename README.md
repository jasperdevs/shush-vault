<div align="center">
  <img src="assets/shh-key-vault-logo.png" alt="shh key vault logo" width="128" />

  <h1>🤫 shh key vault</h1>

  <p><strong>open-source, encrypted secret and API key management for desktop and CLI.</strong></p>

  <p>
    <img alt="GitHub release downloads" src="https://img.shields.io/github/downloads/jasperdevs/shh-key-vault/total?label=downloads">
    <img alt="GitHub latest release downloads" src="https://img.shields.io/github/downloads/jasperdevs/shh-key-vault/latest/total?label=downloads%40latest">
    <img alt="GitHub stars" src="https://img.shields.io/github/stars/jasperdevs/shh-key-vault?style=flat">
  </p>

  <p>
    Shh Key Vault is a local-first vault for storing, organizing, importing, and copying secrets and API keys across macOS, Windows, and Linux.
    The desktop app and CLI should share the same encrypted Rust core so secrets behave the same everywhere.
  </p>

  <h2>what it should do</h2>

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
      <td>CLI for init, add, list, copy, import, export, lock, and unlock</td>
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
      <td>Electron</td>
      <td>Fastest path to a polished cross-platform desktop app with mature packaging for macOS, Windows, and Linux.</td>
    </tr>
    <tr>
      <td>Core logic</td>
      <td>Rust workspace crate</td>
      <td>One shared vault engine for the desktop app and CLI, with encryption kept outside the UI layer.</td>
    </tr>
    <tr>
      <td>CLI</td>
      <td>Rust binary with clap</td>
      <td>Fast startup, easy distribution, and direct reuse of the vault crate.</td>
    </tr>
    <tr>
      <td>UI</td>
      <td>React + TypeScript</td>
      <td>Simple to build the table, dialogs, filters, import preview, and settings screens.</td>
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

  <h2>starter CLI shape</h2>

  <pre><code>shhkeyvault init
shhkeyvault add OPENAI_API_KEY
shhkeyvault list --workspace my-app
shhkeyvault copy OPENAI_API_KEY
shhkeyvault import .env --workspace my-app --env dev
shhkeyvault export --format env</code></pre>

  <p>MIT</p>
</div>
