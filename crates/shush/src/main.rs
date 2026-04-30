use std::fs;
use std::path::PathBuf;

use arboard::Clipboard;
use clap::{Parser, Subcommand};
use shush_vault_core::{decrypt_vault, encrypt_vault, EncryptedVault, SecretRecord, Vault};

#[derive(Parser)]
#[command(name = "shush")]
#[command(about = "local secrets, kept quiet")]
struct Cli {
    #[command(subcommand)]
    command: Command,

    #[arg(long, global = true)]
    vault: Option<PathBuf>,

    #[arg(long, global = true, env = "SHUSH_VAULT_PASSPHRASE")]
    passphrase: Option<String>,
}

#[derive(Subcommand)]
enum Command {
    Init,
    Add {
        key: String,
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
    List {
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
    },
    Import {
        path: PathBuf,
        #[arg(long, default_value = "Default")]
        workspace: String,
        #[arg(long, default_value = "Dev")]
        env: String,
        #[arg(long, default_value = "")]
        provider: String,
    },
    Export {
        #[arg(long)]
        workspace: Option<String>,
        #[arg(long)]
        env: Option<String>,
    },
    Copy {
        key: String,
    },
}

fn main() -> anyhow::Result<()> {
    let cli = Cli::parse();
    let path = cli.vault.unwrap_or_else(default_vault_path);
    let passphrase = cli
        .passphrase
        .filter(|value| !value.is_empty())
        .ok_or_else(|| anyhow::anyhow!("set SHUSH_VAULT_PASSPHRASE or pass --passphrase"))?;

    match cli.command {
        Command::Init => {
            save_vault(&path, &passphrase, &Vault::default())?;
            println!("initialized {}", path.display());
        }
        Command::Add {
            key,
            value,
            workspace,
            env,
            provider,
            notes,
        } => {
            let mut vault = load_vault(&path, &passphrase)?;
            vault.add(SecretRecord::create(&workspace, &key, &value, &env, &provider, &notes));
            save_vault(&path, &passphrase, &vault)?;
            println!("saved {key}");
        }
        Command::List { workspace, env } => {
            let vault = load_vault(&path, &passphrase)?;
            for record in filtered(vault.visible_records(), workspace.as_deref(), env.as_deref()) {
                println!(
                    "{}\t{}\t{}\t{}",
                    record.workspace,
                    record.environment,
                    record.name,
                    mask(&record.value)
                );
            }
        }
        Command::Import {
            path: env_path,
            workspace,
            env,
            provider,
        } => {
            let mut vault = load_vault(&path, &passphrase)?;
            let content = fs::read_to_string(env_path)?;
            for (key, value) in parse_env(&content) {
                vault.add(SecretRecord::create(&workspace, &key, &value, &env, &provider, ".env import"));
            }
            save_vault(&path, &passphrase, &vault)?;
            println!("imported .env entries");
        }
        Command::Export { workspace, env } => {
            let vault = load_vault(&path, &passphrase)?;
            for record in filtered(vault.visible_records(), workspace.as_deref(), env.as_deref()) {
                println!("{}={}", record.name, quote_if_needed(&record.value));
            }
        }
        Command::Copy { key } => {
            let vault = load_vault(&path, &passphrase)?;
            let Some(record) = vault
                .visible_records()
                .find(|record| record.name.eq_ignore_ascii_case(&key))
            else {
                anyhow::bail!("secret not found");
            };
            Clipboard::new()?.set_text(record.value.clone())?;
            println!("copied {}", record.name);
        }
    }

    Ok(())
}

fn default_vault_path() -> PathBuf {
    dirs::data_local_dir()
        .unwrap_or_else(|| std::env::current_dir().expect("current directory"))
        .join("ShushVault")
        .join("vault.shush")
}

fn load_vault(path: &PathBuf, passphrase: &str) -> anyhow::Result<Vault> {
    if !path.exists() {
        return Ok(Vault::default());
    }

    let encrypted: EncryptedVault = serde_json::from_slice(&fs::read(path)?)?;
    Ok(decrypt_vault(&encrypted, passphrase)?)
}

fn save_vault(path: &PathBuf, passphrase: &str, vault: &Vault) -> anyhow::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }

    let encrypted = encrypt_vault(vault, passphrase)?;
    fs::write(path, serde_json::to_vec_pretty(&encrypted)?)?;
    Ok(())
}

fn filtered<'a>(
    records: impl Iterator<Item = &'a SecretRecord>,
    workspace: Option<&str>,
    env: Option<&str>,
) -> impl Iterator<Item = &'a SecretRecord> {
    records.filter(move |record| {
        workspace.is_none_or(|workspace| record.workspace.eq_ignore_ascii_case(workspace))
            && env.is_none_or(|env| record.environment.eq_ignore_ascii_case(env))
    })
}

fn parse_env(content: &str) -> impl Iterator<Item = (String, String)> + '_ {
    content.lines().filter_map(|line| {
        let line = line.trim();
        if line.is_empty() || line.starts_with('#') {
            return None;
        }
        let (key, value) = line.split_once('=')?;
        Some((key.trim().to_owned(), unquote(value.trim()).to_owned()))
    })
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
