use std::fs;
use std::io::{IsTerminal, Write};
use std::path::PathBuf;
use std::thread;
use std::time::Duration;

use arboard::Clipboard;
use clap::{Parser, Subcommand, ValueEnum};
use shush_vault_core::{EncryptedVault, SecretRecord, Vault, decrypt_vault, encrypt_vault};
use zeroize::Zeroizing;

#[derive(Parser)]
#[command(
    name = "shush",
    about = "Local secrets, kept quiet.",
    long_about = "shush is an offline, encrypted vault for API keys and other secrets.\n\
                  Everything is stored in a single file with AES-256-GCM and PBKDF2-SHA256.\n\
                  Run `shush init` once, then add, list, or copy values.",
    version
)]
struct Cli {
    #[command(subcommand)]
    command: Command,

    #[arg(
        long,
        global = true,
        help = "Path to the vault file (defaults to your local data directory)."
    )]
    vault: Option<PathBuf>,

    #[arg(
        long,
        global = true,
        env = "SHUSH_VAULT_PASSPHRASE",
        help = "Vault passphrase. Prefer the hidden interactive prompt for everyday use."
    )]
    passphrase: Option<String>,

    #[arg(
        long,
        global = true,
        help = "Disable colored output even when writing to a terminal."
    )]
    no_color: bool,
}

#[derive(Subcommand)]
enum Command {
    /// Create a fresh, empty vault.
    #[command(alias = "new")]
    Init {
        /// Overwrite an existing vault file. Existing secrets cannot be recovered.
        #[arg(long)]
        force: bool,
    },

    /// Print the absolute path of the vault file.
    Path,

    /// Add a new secret.
    #[command(alias = "set")]
    Add {
        /// Secret key (e.g. OPENAI_API_KEY).
        key: String,
        /// Secret value.
        value: String,
        #[arg(long, default_value = "Default")]
        workspace: String,
        #[arg(long, default_value = "Dev")]
        env: String,
        #[arg(long, default_value = "")]
        provider: String,
        #[arg(long, default_value = "")]
        notes: String,
    },

    /// List secrets in the vault.
    #[command(alias = "ls")]
    List {
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
        #[arg(long)]
        search: Option<String>,
        /// Emit the listing as JSON instead of a table.
        #[arg(long)]
        json: bool,
    },

    /// Reveal a secret's value to stdout.
    #[command(alias = "show")]
    Get {
        key: String,
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
    },

    /// Update an existing secret.
    Update {
        key: String,
        #[arg(long)]
        value: Option<String>,
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
        #[arg(long)]
        provider: Option<String>,
        #[arg(long)]
        notes: Option<String>,
    },

    /// Remove a secret.
    #[command(alias = "rm", alias = "remove")]
    Delete {
        key: String,
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
        /// Skip the confirmation prompt.
        #[arg(long, short = 'y')]
        yes: bool,
    },

    /// Import KEY=value lines from a .env file.
    Import {
        path: PathBuf,
        #[arg(long, default_value = "Default")]
        workspace: String,
        #[arg(long, default_value = "Dev")]
        env: String,
        #[arg(long, default_value = "")]
        provider: String,
        #[arg(long, default_value = "skip")]
        conflict: ConflictMode,
        /// Print what would be imported without modifying the vault.
        #[arg(long)]
        preview: bool,
    },

    /// Export visible secrets as a .env-style payload.
    Export {
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
        #[arg(long)]
        output: Option<PathBuf>,
        /// Print the payload to stdout (writes plaintext, so use with care).
        #[arg(long)]
        stdout: bool,
    },

    /// Copy a secret to the clipboard, then clear it.
    #[command(alias = "cp")]
    Copy {
        key: String,
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
        /// Seconds before the clipboard is wiped. Pass 0 to keep it.
        #[arg(long, default_value_t = 30)]
        clear_after: u64,
    },
}

#[derive(Clone, Copy, ValueEnum)]
enum ConflictMode {
    Skip,
    Overwrite,
    Rename,
}

fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    let theme = Theme::from_env(cli.no_color);
    let path = cli.vault.unwrap_or_else(default_vault_path);

    if let Err(error) = run(cli.command, cli.passphrase, &path, &theme) {
        eprintln!("{} {error}", theme.error("error:"));
        std::process::exit(1);
    }
    Ok(())
}

fn run(
    command: Command,
    passphrase_arg: Option<String>,
    path: &PathBuf,
    theme: &Theme,
) -> anyhow::Result<()> {
    match command {
        Command::Init { force } => {
            if path.exists() && !force {
                anyhow::bail!(
                    "{} already exists; pass --force to overwrite",
                    path.display()
                );
            }
            let passphrase = resolve_passphrase(passphrase_arg, true)?;
            save_vault(path, passphrase.as_str(), &Vault::default())?;
            println!(
                "{} initialized vault at {}",
                theme.success("✓"),
                theme.dim(&path.display().to_string())
            );
        }

        Command::Path => {
            println!("{}", path.display());
        }

        Command::Add {
            key,
            value,
            workspace,
            env,
            provider,
            notes,
        } => {
            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let mut vault = load_vault(path, passphrase.as_str())?;
            vault.add(SecretRecord::create(
                &workspace, &key, &value, &env, &provider, &notes,
            ));
            save_vault(path, passphrase.as_str(), &vault)?;
            println!(
                "{} saved {} {}",
                theme.success("✓"),
                theme.key(&key),
                theme.dim(&format!("({workspace} · {env})"))
            );
        }

        Command::List {
            workspace,
            env,
            search,
            json,
        } => {
            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let vault = load_vault(path, passphrase.as_str())?;
            let visible: Vec<&SecretRecord> = filtered(
                vault.visible_records(),
                workspace.as_deref(),
                env.as_deref(),
                search.as_deref(),
            )
            .collect();

            if json {
                print_list_json(&visible)?;
            } else if visible.is_empty() {
                println!("{}", theme.dim("no secrets match those filters"));
            } else {
                print_list_table(&visible, theme);
            }
        }

        Command::Get {
            key,
            workspace,
            env,
        } => {
            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let vault = load_vault(path, passphrase.as_str())?;
            let record = vault
                .find(&key, workspace.as_deref(), env.as_deref())
                .ok_or_else(|| not_found_error(&key, workspace.as_deref(), env.as_deref()))?;
            println!("{}", record.value);
        }

        Command::Update {
            key,
            value,
            workspace,
            env,
            provider,
            notes,
        } => {
            if value.is_none() && provider.is_none() && notes.is_none() {
                anyhow::bail!("nothing to update; pass --value, --provider, or --notes");
            }

            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let mut vault = load_vault(path, passphrase.as_str())?;
            if !vault.update(
                &key,
                workspace.as_deref(),
                env.as_deref(),
                value.as_deref(),
                provider.as_deref(),
                notes.as_deref(),
            ) {
                anyhow::bail!(not_found_error(
                    &key,
                    workspace.as_deref(),
                    env.as_deref()
                ));
            }

            save_vault(path, passphrase.as_str(), &vault)?;
            println!("{} updated {}", theme.success("✓"), theme.key(&key));
        }

        Command::Delete {
            key,
            workspace,
            env,
            yes,
        } => {
            if !yes && !confirm(&format!("Delete {key}?"))? {
                println!("{}", theme.dim("aborted"));
                return Ok(());
            }

            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let mut vault = load_vault(path, passphrase.as_str())?;
            if !vault.delete(&key, workspace.as_deref(), env.as_deref()) {
                anyhow::bail!(not_found_error(
                    &key,
                    workspace.as_deref(),
                    env.as_deref()
                ));
            }

            save_vault(path, passphrase.as_str(), &vault)?;
            println!("{} deleted {}", theme.success("✓"), theme.key(&key));
        }

        Command::Import {
            path: env_path,
            workspace,
            env,
            provider,
            conflict,
            preview,
        } => {
            let content = fs::read_to_string(&env_path)
                .map_err(|e| anyhow::anyhow!("could not read {}: {e}", env_path.display()))?;
            let items = preview_env(&content);

            if preview {
                for item in items {
                    let key = item.key.as_deref().unwrap_or("-");
                    let line = format!("{:>4}", item.line);
                    println!(
                        "{}  {}  {}",
                        theme.dim(&line),
                        theme.status(item.status),
                        theme.key(key)
                    );
                }
                return Ok(());
            }

            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let mut vault = load_vault(path, passphrase.as_str())?;
            let mut imported = 0;
            let mut skipped = 0;
            let mut invalid = 0;
            for item in items {
                match item.status {
                    "ignored" => continue,
                    "invalid" => {
                        invalid += 1;
                        continue;
                    }
                    "ready" => {}
                    _ => continue,
                }

                let key = item.key.expect("ready item has key");
                let value = item.value.expect("ready item has value");
                let existing = vault.find(&key, Some(&workspace), Some(&env)).is_some();

                if existing && matches!(conflict, ConflictMode::Skip) {
                    skipped += 1;
                    continue;
                }

                if existing && matches!(conflict, ConflictMode::Overwrite) {
                    vault.update(
                        &key,
                        Some(&workspace),
                        Some(&env),
                        Some(&value),
                        Some(&provider),
                        None,
                    );
                    imported += 1;
                    continue;
                }

                let key = if existing {
                    format!("{key}_{}", chrono::Utc::now().format("%Y%m%d%H%M%S"))
                } else {
                    key
                };

                vault.add(SecretRecord::create(
                    &workspace,
                    &key,
                    &value,
                    &env,
                    &provider,
                    ".env import",
                ));
                imported += 1;
            }
            save_vault(path, passphrase.as_str(), &vault)?;
            println!(
                "{} imported {imported} {} {} skipped, {} invalid",
                theme.success("✓"),
                theme.dim("·"),
                skipped,
                invalid
            );
        }

        Command::Export {
            workspace,
            env,
            output,
            stdout,
        } => {
            if output.is_none() && !stdout {
                anyhow::bail!(
                    "export writes plaintext; pass --stdout or --output <path>"
                );
            }

            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let vault = load_vault(path, passphrase.as_str())?;
            let content = filtered(
                vault.visible_records(),
                workspace.as_deref(),
                env.as_deref(),
                None,
            )
            .map(|record| format!("{}={}", record.name, quote_if_needed(&record.value)))
            .collect::<Vec<_>>()
            .join("\n");

            if stdout {
                println!("{content}");
            }

            if let Some(output) = output {
                fs::write(&output, &content)?;
                eprintln!(
                    "{} wrote {} bytes to {}",
                    theme.success("✓"),
                    content.len(),
                    theme.dim(&output.display().to_string())
                );
            }
        }

        Command::Copy {
            key,
            workspace,
            env,
            clear_after,
        } => {
            let passphrase = resolve_passphrase(passphrase_arg, false)?;
            let vault = load_vault(path, passphrase.as_str())?;
            let record = vault
                .find(&key, workspace.as_deref(), env.as_deref())
                .ok_or_else(|| not_found_error(&key, workspace.as_deref(), env.as_deref()))?;
            let copied_value = record.value.clone();
            let mut clipboard = Clipboard::new()?;
            clipboard.set_text(copied_value.clone())?;
            if clear_after == 0 {
                println!(
                    "{} copied {}",
                    theme.success("✓"),
                    theme.key(&record.name)
                );
            } else {
                println!(
                    "{} copied {} {}",
                    theme.success("✓"),
                    theme.key(&record.name),
                    theme.dim(&format!("(clearing in {clear_after}s)"))
                );
                thread::sleep(Duration::from_secs(clear_after));
                if clipboard.get_text().is_ok_and(|text| text == copied_value) {
                    clipboard.set_text(String::new())?;
                    eprintln!("{} cleared clipboard", theme.dim("·"));
                }
            }
        }
    }

    Ok(())
}

fn confirm(prompt: &str) -> anyhow::Result<bool> {
    if !std::io::stdin().is_terminal() {
        return Ok(false);
    }
    print!("{prompt} [y/N] ");
    std::io::stdout().flush()?;
    let mut answer = String::new();
    std::io::stdin().read_line(&mut answer)?;
    Ok(matches!(answer.trim(), "y" | "Y" | "yes" | "YES"))
}

fn not_found_error(key: &str, workspace: Option<&str>, env: Option<&str>) -> anyhow::Error {
    let mut detail = format!("no secret named {key}");
    if let Some(workspace) = workspace {
        detail.push_str(&format!(" in workspace {workspace}"));
    }
    if let Some(env) = env {
        detail.push_str(&format!(" / env {env}"));
    }
    anyhow::anyhow!(detail)
}

fn print_list_table(records: &[&SecretRecord], theme: &Theme) {
    let env_width = records
        .iter()
        .map(|r| r.environment.len())
        .max()
        .unwrap_or(3)
        .max(3);
    let workspace_width = records
        .iter()
        .map(|r| r.workspace.len())
        .max()
        .unwrap_or(9)
        .max(9);
    let key_width = records
        .iter()
        .map(|r| r.name.len())
        .max()
        .unwrap_or(3)
        .max(3);

    let header = format!(
        "{:env_width$}  {:workspace_width$}  {:key_width$}  {}",
        "ENV",
        "WORKSPACE",
        "KEY",
        "VALUE",
        env_width = env_width,
        workspace_width = workspace_width,
        key_width = key_width,
    );
    println!("{}", theme.header(&header));

    for record in records {
        let env = format!("{:env_width$}", record.environment, env_width = env_width);
        let workspace = format!(
            "{:workspace_width$}",
            record.workspace,
            workspace_width = workspace_width
        );
        let name = format!("{:key_width$}", record.name, key_width = key_width);
        println!(
            "{}  {}  {}  {}",
            theme.env(&record.environment, &env),
            workspace,
            theme.key(&name),
            theme.dim(&mask(&record.value))
        );
    }
}

fn print_list_json(records: &[&SecretRecord]) -> anyhow::Result<()> {
    let serializable: Vec<serde_json::Value> = records
        .iter()
        .map(|record| {
            serde_json::json!({
                "id": record.id.to_string(),
                "workspace": record.workspace,
                "key": record.name,
                "environment": record.environment,
                "provider": record.provider,
                "notes": record.notes,
                "masked": mask(&record.value),
                "createdAt": record.created_at.to_rfc3339(),
                "updatedAt": record.updated_at.to_rfc3339(),
            })
        })
        .collect();
    println!("{}", serde_json::to_string_pretty(&serializable)?);
    Ok(())
}

fn default_vault_path() -> PathBuf {
    dirs::data_local_dir()
        .unwrap_or_else(|| std::env::current_dir().expect("current directory"))
        .join("ShushVault")
        .join("vault.shush")
}

fn resolve_passphrase(
    passphrase: Option<String>,
    confirm: bool,
) -> anyhow::Result<Zeroizing<String>> {
    if let Some(passphrase) = passphrase.filter(|value| !value.is_empty()) {
        return Ok(Zeroizing::new(passphrase));
    }

    let entered = Zeroizing::new(rpassword::prompt_password("Vault passphrase: ")?);
    if confirm {
        let confirmed = Zeroizing::new(rpassword::prompt_password("Confirm passphrase: ")?);
        if confirmed.as_str() != entered.as_str() {
            anyhow::bail!("passphrases do not match");
        }
    }
    Ok(entered)
}

fn load_vault(path: &PathBuf, passphrase: &str) -> anyhow::Result<Vault> {
    if !path.exists() {
        anyhow::bail!(
            "no vault at {}; run `shush init` to create one",
            path.display()
        );
    }

    let bytes = fs::read(path)
        .map_err(|e| anyhow::anyhow!("could not read {}: {e}", path.display()))?;
    let encrypted: EncryptedVault = serde_json::from_slice(&bytes)
        .map_err(|e| anyhow::anyhow!("vault file is not valid: {e}"))?;
    decrypt_vault(&encrypted, passphrase).map_err(|_| anyhow::anyhow!("wrong passphrase"))
}

fn save_vault(path: &PathBuf, passphrase: &str, vault: &Vault) -> anyhow::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }

    let encrypted = encrypt_vault(vault, passphrase)?;
    write_vault_atomically(path, &serde_json::to_vec_pretty(&encrypted)?)?;
    Ok(())
}

fn write_vault_atomically(path: &PathBuf, contents: &[u8]) -> anyhow::Result<()> {
    let temp_path = path.with_extension("shush.tmp");
    {
        let mut file = fs::File::create(&temp_path)?;
        std::io::Write::write_all(&mut file, contents)?;
        file.sync_all()?;
    }

    replace_file(&temp_path, path)?;
    if let Some(parent) = path.parent() {
        let _ = fs::File::open(parent).and_then(|dir| dir.sync_all());
    }

    Ok(())
}

#[cfg(not(windows))]
fn replace_file(temp_path: &PathBuf, path: &PathBuf) -> anyhow::Result<()> {
    fs::rename(temp_path, path)?;
    Ok(())
}

#[cfg(windows)]
fn replace_file(temp_path: &PathBuf, path: &PathBuf) -> anyhow::Result<()> {
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Storage::FileSystem::{
        MOVEFILE_REPLACE_EXISTING, MOVEFILE_WRITE_THROUGH, MoveFileExW,
    };

    let mut temp: Vec<u16> = temp_path.as_os_str().encode_wide().chain(Some(0)).collect();
    let mut destination: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
    let result = unsafe {
        MoveFileExW(
            temp.as_mut_ptr(),
            destination.as_mut_ptr(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH,
        )
    };
    if result == 0 {
        return Err(std::io::Error::last_os_error().into());
    }

    Ok(())
}

fn filtered<'a>(
    records: impl Iterator<Item = &'a SecretRecord>,
    workspace: Option<&str>,
    env: Option<&str>,
    search: Option<&str>,
) -> impl Iterator<Item = &'a SecretRecord> {
    records.filter(move |record| {
        workspace.is_none_or(|workspace| record.workspace.eq_ignore_ascii_case(workspace))
            && env.is_none_or(|env| record.environment.eq_ignore_ascii_case(env))
            && search.is_none_or(|search| {
                let search = search.to_lowercase();
                record.name.to_lowercase().contains(&search)
                    || record.provider.to_lowercase().contains(&search)
                    || record.notes.to_lowercase().contains(&search)
                    || record.workspace.to_lowercase().contains(&search)
                    || record.environment.to_lowercase().contains(&search)
            })
    })
}

struct EnvPreviewItem {
    line: usize,
    key: Option<String>,
    value: Option<String>,
    status: &'static str,
}

fn preview_env(content: &str) -> Vec<EnvPreviewItem> {
    content
        .lines()
        .enumerate()
        .map(|(index, line)| {
            let line_number = index + 1;
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                return EnvPreviewItem {
                    line: line_number,
                    key: None,
                    value: None,
                    status: "ignored",
                };
            }

            let Some((key, value)) = line.split_once('=') else {
                return EnvPreviewItem {
                    line: line_number,
                    key: None,
                    value: None,
                    status: "invalid",
                };
            };

            EnvPreviewItem {
                line: line_number,
                key: Some(key.trim().to_owned()),
                value: Some(unquote(value.trim()).to_owned()),
                status: "ready",
            }
        })
        .collect()
}

fn unquote(value: &str) -> &str {
    if value.len() >= 2
        && ((value.starts_with('"') && value.ends_with('"'))
            || (value.starts_with('\'') && value.ends_with('\'')))
    {
        &value[1..value.len() - 1]
    } else {
        value
    }
}

fn quote_if_needed(value: &str) -> String {
    if value.chars().any(char::is_whitespace) || value.contains('#') || value.contains('"') {
        format!("\"{}\"", value.replace('"', "\\\""))
    } else {
        value.to_owned()
    }
}

fn mask(value: &str) -> String {
    if value.len() <= 4 {
        "•".repeat(value.len().max(1))
    } else {
        format!("{}{}", "•".repeat(8), &value[value.len() - 4..])
    }
}

struct Theme {
    enabled: bool,
}

impl Theme {
    fn from_env(no_color: bool) -> Self {
        let enabled = !no_color
            && std::env::var_os("NO_COLOR").is_none()
            && std::io::stdout().is_terminal();
        Self { enabled }
    }

    fn paint(&self, value: &str, code: &str) -> String {
        if self.enabled {
            format!("\u{1b}[{code}m{value}\u{1b}[0m")
        } else {
            value.to_owned()
        }
    }

    fn success(&self, value: &str) -> String {
        self.paint(value, "32") // green
    }
    fn error(&self, value: &str) -> String {
        self.paint(value, "1;31") // bold red
    }
    fn key(&self, value: &str) -> String {
        self.paint(value, "1;36") // bold cyan
    }
    fn dim(&self, value: &str) -> String {
        self.paint(value, "2") // dim
    }
    fn header(&self, value: &str) -> String {
        self.paint(value, "2;1") // dim+bold for column headers
    }
    fn env(&self, environment: &str, padded: &str) -> String {
        let code = match environment {
            "Dev" => "36",      // cyan
            "Staging" => "33",  // yellow
            "Prod" => "31",     // red
            _ => "0",
        };
        if self.enabled && code != "0" {
            self.paint(padded, code)
        } else {
            padded.to_owned()
        }
    }
    fn status(&self, value: &str) -> String {
        let code = match value {
            "ready" => "32",
            "ignored" => "2",
            "invalid" => "31",
            _ => "33",
        };
        self.paint(value, code)
    }
}
