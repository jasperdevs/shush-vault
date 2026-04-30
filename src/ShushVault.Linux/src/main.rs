use std::cell::RefCell;
use std::fs;
use std::path::PathBuf;
use std::rc::Rc;

use gtk::prelude::*;
use gtk::{
    glib, Application, ApplicationWindow, Box, Button, Entry, Label, ListBox, Orientation,
    PasswordEntry,
};
use shush_vault_core::{decrypt_vault, encrypt_vault, EncryptedVault, SecretRecord, Vault};

const APP_ID: &str = "dev.jasper.shushvault";

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
}

fn build_ui(app: &Application) {
    let vault = Rc::new(RefCell::new(Vault::default()));
    let passphrase_state = Rc::new(RefCell::new(String::new()));
    let vault_path = default_vault_path();

    let root = Box::new(Orientation::Horizontal, 18);
    root.set_margin_top(24);
    root.set_margin_bottom(24);
    root.set_margin_start(24);
    root.set_margin_end(24);

    let editor = Box::new(Orientation::Vertical, 12);
    let title = Label::new(Some("shush vault"));
    title.add_css_class("title-1");

    let passphrase = PasswordEntry::builder()
        .placeholder_text("Vault passphrase")
        .build();
    let unlock = Button::with_label("Unlock");
    let workspace = Entry::builder()
        .placeholder_text("Workspace")
        .text("Default")
        .build();
    let key = Entry::builder().placeholder_text("Key").build();
    let value = PasswordEntry::builder().placeholder_text("Value").build();
    let environment = Entry::builder()
        .placeholder_text("Environment")
        .text("Dev")
        .build();
    let provider = Entry::builder().placeholder_text("Provider").build();
    let notes = Entry::builder().placeholder_text("Notes").build();
    let save = Button::with_label("Save");
    let status = Label::new(Some("Locked"));
    status.set_xalign(0.0);

    editor.append(&title);
    editor.append(&passphrase);
    editor.append(&unlock);
    editor.append(&workspace);
    editor.append(&key);
    editor.append(&value);
    editor.append(&environment);
    editor.append(&provider);
    editor.append(&notes);
    editor.append(&save);
    editor.append(&status);

    let list = ListBox::new();
    unlock.connect_clicked(glib::clone!(
        @weak passphrase,
        @weak list,
        @weak status,
        @strong vault,
        @strong passphrase_state,
        @strong vault_path
        => move |_| {
            let passphrase_text = passphrase.text().to_string();
            if passphrase_text.trim().is_empty() {
                status.set_text("Enter a passphrase.");
                return;
            }

            match load_vault(&vault_path, &passphrase_text) {
                Ok(loaded) => {
                    *vault.borrow_mut() = loaded;
                    *passphrase_state.borrow_mut() = passphrase_text;
                    render_list(&list, &vault.borrow());
                    status.set_text("Unlocked encrypted vault.");
                }
                Err(_) => status.set_text("Could not unlock vault."),
            }
        }
    ));

    save.connect_clicked(glib::clone!(
        @weak workspace,
        @weak key,
        @weak value,
        @weak environment,
        @weak provider,
        @weak notes,
        @weak list,
        @weak status,
        @strong vault,
        @strong passphrase_state,
        @strong vault_path
        => move |_| {
            let passphrase = passphrase_state.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            if key.text().trim().is_empty() || value.text().is_empty() {
                status.set_text("Key and value are required.");
                return;
            }

            vault.borrow_mut().add(SecretRecord::create(
                workspace.text().as_str(),
                key.text().as_str(),
                value.text().as_str(),
                environment.text().as_str(),
                provider.text().as_str(),
                notes.text().as_str(),
            ));

            if save_vault(&vault_path, &passphrase, &vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            render_list(&list, &vault.borrow());
            key.set_text("");
            value.set_text("");
            provider.set_text("");
            notes.set_text("");
            status.set_text("Saved to encrypted vault.");
        }
    ));

    root.append(&editor);
    root.append(&list);

    let window = ApplicationWindow::builder()
        .application(app)
        .title("Shush Vault")
        .default_width(980)
        .default_height(680)
        .child(&root)
        .build();

    window.present();
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

fn render_list(list: &ListBox, vault: &Vault) {
    while let Some(child) = list.first_child() {
        list.remove(&child);
    }

    for record in vault.visible_records() {
        let label = Label::new(Some(&format!(
            "{}    {}    {}    {}",
            record.workspace,
            record.name,
            mask(&record.value),
            record.provider
        )));
        label.set_xalign(0.0);
        list.append(&label);
    }
}

fn mask(value: &str) -> String {
    if value.len() <= 4 {
        "•".repeat(value.len().max(1))
    } else {
        format!("{}{}", "•".repeat(8), &value[value.len() - 4..])
    }
}
