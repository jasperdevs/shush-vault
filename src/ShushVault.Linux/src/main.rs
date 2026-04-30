use std::cell::RefCell;
use std::fs;
use std::path::PathBuf;
use std::rc::Rc;

use gtk::gdk::Display;
use gtk::prelude::*;
use gtk::{
    glib, Application, ApplicationWindow, Box, Button, ComboBoxText, Entry, Label, ListBox,
    Orientation, PasswordEntry, ScrolledWindow, TextView,
};
use shush_vault_core::{decrypt_vault, encrypt_vault, EncryptedVault, SecretRecord, Vault};

const APP_ID: &str = "dev.jasper.shushvault";
const CLIPBOARD_CLEAR_SECONDS: u32 = 30;

#[derive(Clone, Copy, PartialEq, Eq)]
enum ConflictMode {
    Skip,
    Overwrite,
    Rename,
}

struct ImportStats {
    imported: usize,
    skipped: usize,
    invalid: usize,
}

struct EnvItem {
    key: String,
    value: String,
}

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
}

fn build_ui(app: &Application) {
    let vault = Rc::new(RefCell::new(Vault::default()));
    let passphrase_state = Rc::new(RefCell::new(String::new()));
    let selected_id = Rc::new(RefCell::new(None::<String>));
    let editing_id = Rc::new(RefCell::new(None::<String>));
    let visible_ids = Rc::new(RefCell::new(Vec::<String>::new()));
    let vault_path = default_vault_path();

    let root = Box::new(Orientation::Vertical, 14);
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
    let clear = Button::with_label("Clear");
    let editor_actions = Box::new(Orientation::Horizontal, 8);
    editor_actions.append(&save);
    editor_actions.append(&clear);

    let import_label = Label::new(Some(".env import"));
    import_label.set_xalign(0.0);
    import_label.add_css_class("heading");

    let conflict = ComboBoxText::new();
    conflict.append_text("Skip");
    conflict.append_text("Overwrite");
    conflict.append_text("Rename");
    conflict.set_active(Some(0));

    let import_text = TextView::new();
    import_text.set_monospace(true);
    import_text.set_wrap_mode(gtk::WrapMode::WordChar);
    let import_scroll = ScrolledWindow::new();
    import_scroll.set_min_content_height(120);
    import_scroll.set_child(Some(&import_text));

    let paste_env = Button::with_label("Paste clipboard");
    let import_env = Button::with_label("Import .env");
    let import_actions = Box::new(Orientation::Horizontal, 8);
    import_actions.append(&paste_env);
    import_actions.append(&import_env);

    let status = Label::new(Some("Locked"));
    status.set_xalign(0.0);
    status.set_wrap(true);

    editor.append(&title);
    editor.append(&passphrase);
    editor.append(&unlock);
    editor.append(&workspace);
    editor.append(&key);
    editor.append(&value);
    editor.append(&environment);
    editor.append(&provider);
    editor.append(&notes);
    editor.append(&editor_actions);
    editor.append(&import_label);
    editor.append(&conflict);
    editor.append(&import_scroll);
    editor.append(&import_actions);
    editor.append(&status);

    let list_panel = Box::new(Orientation::Vertical, 12);
    let search = Entry::builder().placeholder_text("Search secrets").build();
    let workspace_filter = Entry::builder()
        .placeholder_text("Workspace filter")
        .build();
    let environment_filter = ComboBoxText::new();
    environment_filter.append_text("All");
    environment_filter.append_text("Dev");
    environment_filter.append_text("Staging");
    environment_filter.append_text("Prod");
    environment_filter.set_active(Some(0));

    let filters = Box::new(Orientation::Horizontal, 8);
    filters.append(&search);
    filters.append(&workspace_filter);
    filters.append(&environment_filter);

    let edit_selected = Button::with_label("Edit selected");
    let copy_selected = Button::with_label("Copy value");
    let delete_selected = Button::with_label("Delete");
    let export_visible = Button::with_label("Export .env");
    let list_actions = Box::new(Orientation::Horizontal, 8);
    list_actions.append(&edit_selected);
    list_actions.append(&copy_selected);
    list_actions.append(&delete_selected);
    list_actions.append(&export_visible);

    let list = ListBox::new();
    list.set_vexpand(true);
    let list_scroll = ScrolledWindow::new();
    list_scroll.set_vexpand(true);
    list_scroll.set_child(Some(&list));

    list_panel.append(&filters);
    list_panel.append(&list_actions);
    list_panel.append(&list_scroll);

    list.connect_row_selected(glib::clone!(
        @strong selected_id,
        @strong visible_ids
        => move |_, row| {
            let Some(row) = row else {
                *selected_id.borrow_mut() = None;
                return;
            };

            let index = row.index();
            let selected = usize::try_from(index)
                .ok()
                .and_then(|index| visible_ids.borrow().get(index).cloned());
            *selected_id.borrow_mut() = selected;
        }
    ));

    unlock.connect_clicked(glib::clone!(
        @weak passphrase,
        @weak list,
        @weak status,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @strong vault,
        @strong passphrase_state,
        @strong selected_id,
        @strong editing_id,
        @strong visible_ids,
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
                    passphrase.set_text("");
                    *selected_id.borrow_mut() = None;
                    *editing_id.borrow_mut() = None;
                    let count = render_list(
                        &list,
                        &vault.borrow(),
                        search.text().as_str(),
                        workspace_filter.text().as_str(),
                        active_environment_filter(&environment_filter).as_deref(),
                        &visible_ids,
                        &selected_id,
                    );
                    status.set_text(&format!("Unlocked encrypted vault. {count} secret(s) shown."));
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
        @weak save,
        @weak list,
        @weak status,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @strong vault,
        @strong passphrase_state,
        @strong selected_id,
        @strong editing_id,
        @strong visible_ids,
        @strong vault_path
        => move |_| {
            let passphrase = passphrase_state.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            let workspace_text = workspace.text().to_string();
            let key_text = key.text().to_string();
            let value_text = value.text().to_string();
            let environment_text = environment.text().to_string();
            let provider_text = provider.text().to_string();
            let notes_text = notes.text().to_string();

            if key_text.trim().is_empty() || value_text.is_empty() {
                status.set_text("Key and value are required.");
                return;
            }

            let message = if let Some(id) = editing_id.borrow().clone() {
                let mut vault_ref = vault.borrow_mut();
                let Some(record) = find_record_mut_by_id(&mut *vault_ref, &id) else {
                    status.set_text("Selected secret no longer exists.");
                    return;
                };

                replace_record(
                    record,
                    &workspace_text,
                    &key_text,
                    &value_text,
                    &environment_text,
                    &provider_text,
                    &notes_text,
                );
                "Updated encrypted vault."
            } else {
                vault.borrow_mut().add(SecretRecord::create(
                    &workspace_text,
                    &key_text,
                    &value_text,
                    &environment_text,
                    &provider_text,
                    &notes_text,
                ));
                "Saved to encrypted vault."
            };

            if save_vault(&vault_path, &passphrase, &vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            key.set_text("");
            value.set_text("");
            provider.set_text("");
            notes.set_text("");
            save.set_label("Save");
            *editing_id.borrow_mut() = None;
            *selected_id.borrow_mut() = None;
            render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text(message);
        }
    ));

    clear.connect_clicked(glib::clone!(
        @weak key,
        @weak value,
        @weak provider,
        @weak notes,
        @weak save,
        @weak status,
        @strong editing_id
        => move |_| {
            key.set_text("");
            value.set_text("");
            provider.set_text("");
            notes.set_text("");
            save.set_label("Save");
            *editing_id.borrow_mut() = None;
            status.set_text("Editor cleared.");
        }
    ));

    edit_selected.connect_clicked(glib::clone!(
        @weak workspace,
        @weak key,
        @weak value,
        @weak environment,
        @weak provider,
        @weak notes,
        @weak save,
        @weak status,
        @strong vault,
        @strong selected_id,
        @strong editing_id
        => move |_| {
            let Some(id) = selected_id.borrow().clone() else {
                status.set_text("Select a secret to edit.");
                return;
            };

            let vault_ref = vault.borrow();
            let Some(record) = find_record_by_id(&vault_ref, &id) else {
                status.set_text("Selected secret no longer exists.");
                return;
            };

            workspace.set_text(&record.workspace);
            key.set_text(&record.name);
            value.set_text(&record.value);
            environment.set_text(&record.environment);
            provider.set_text(&record.provider);
            notes.set_text(&record.notes);
            save.set_label("Update");
            *editing_id.borrow_mut() = Some(id);
            status.set_text("Editing selected secret.");
        }
    ));

    delete_selected.connect_clicked(glib::clone!(
        @weak list,
        @weak status,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @strong vault,
        @strong passphrase_state,
        @strong selected_id,
        @strong editing_id,
        @strong visible_ids,
        @strong vault_path
        => move |_| {
            let passphrase = passphrase_state.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            let Some(id) = selected_id.borrow().clone() else {
                status.set_text("Select a secret to delete.");
                return;
            };

            let before = vault.borrow().records.len();
            vault.borrow_mut().records.retain(|record| record.id.to_string() != id);
            if before == vault.borrow().records.len() {
                status.set_text("Selected secret no longer exists.");
                return;
            }

            if save_vault(&vault_path, &passphrase, &vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            *selected_id.borrow_mut() = None;
            *editing_id.borrow_mut() = None;
            render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text("Deleted selected secret.");
        }
    ));

    copy_selected.connect_clicked(glib::clone!(
        @weak status,
        @strong vault,
        @strong passphrase_state,
        @strong selected_id
        => move |_| {
            if passphrase_state.borrow().is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            let Some(id) = selected_id.borrow().clone() else {
                status.set_text("Select a secret to copy.");
                return;
            };

            let vault_ref = vault.borrow();
            let Some(record) = find_record_by_id(&vault_ref, &id) else {
                status.set_text("Selected secret no longer exists.");
                return;
            };

            if copy_text_with_delayed_clear(&record.value) {
                status.set_text(&format!(
                    "Copied {}. Clipboard clears in {CLIPBOARD_CLEAR_SECONDS}s.",
                    record.name
                ));
            } else {
                status.set_text("Clipboard is not available.");
            }
        }
    ));

    paste_env.connect_clicked(glib::clone!(
        @weak import_text,
        @weak status
        => move |_| {
            let Some(display) = Display::default() else {
                status.set_text("Clipboard is not available.");
                return;
            };

            let clipboard = display.clipboard();
            glib::MainContext::default().spawn_local(glib::clone!(
                @weak import_text,
                @weak status
                => async move {
                    match clipboard.read_text_future().await {
                        Ok(Some(text)) => {
                            import_text.buffer().set_text(text.as_str());
                            status.set_text("Pasted clipboard text into .env import.");
                        }
                        Ok(None) => status.set_text("Clipboard does not contain text."),
                        Err(_) => status.set_text("Could not read clipboard text."),
                    }
                }
            ));
        }
    ));

    import_env.connect_clicked(glib::clone!(
        @weak import_text,
        @weak workspace,
        @weak environment,
        @weak provider,
        @weak conflict,
        @weak list,
        @weak status,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @strong vault,
        @strong passphrase_state,
        @strong selected_id,
        @strong visible_ids,
        @strong vault_path
        => move |_| {
            let passphrase = passphrase_state.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            let content = text_view_text(&import_text);
            if content.trim().is_empty() {
                status.set_text("Paste KEY=value lines first.");
                return;
            }

            let stats = {
                let mut vault_ref = vault.borrow_mut();
                import_env_records(
                    &mut *vault_ref,
                    &content,
                    workspace.text().as_str(),
                    environment.text().as_str(),
                    provider.text().as_str(),
                    selected_conflict_mode(&conflict),
                )
            };

            if stats.imported > 0 && save_vault(&vault_path, &passphrase, &vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            if stats.imported > 0 {
                import_text.buffer().set_text("");
            }
            *selected_id.borrow_mut() = None;
            render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text(&format!(
                "Imported {}, skipped {}, invalid {}.",
                stats.imported, stats.skipped, stats.invalid
            ));
        }
    ));

    export_visible.connect_clicked(glib::clone!(
        @weak status,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @strong vault,
        @strong passphrase_state
        => move |_| {
            if passphrase_state.borrow().is_empty() {
                status.set_text("Unlock the vault first.");
                return;
            }

            let content = visible_records(
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
            )
            .into_iter()
            .map(|record| format!("{}={}", record.name, quote_if_needed(&record.value)))
            .collect::<Vec<_>>()
            .join("\n");

            if content.is_empty() {
                status.set_text("No visible secrets to export.");
                return;
            }

            if copy_text_with_delayed_clear(&content) {
                status.set_text(&format!(
                    "Copied visible secrets as .env. Clipboard clears in {CLIPBOARD_CLEAR_SECONDS}s."
                ));
            } else {
                status.set_text("Clipboard is not available.");
            }
        }
    ));

    search.connect_changed(glib::clone!(
        @weak list,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @weak status,
        @strong vault,
        @strong visible_ids,
        @strong selected_id
        => move |_| {
            let count = render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text(&format!("{count} secret(s) shown."));
        }
    ));

    workspace_filter.connect_changed(glib::clone!(
        @weak list,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @weak status,
        @strong vault,
        @strong visible_ids,
        @strong selected_id
        => move |_| {
            let count = render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text(&format!("{count} secret(s) shown."));
        }
    ));

    environment_filter.connect_changed(glib::clone!(
        @weak list,
        @weak search,
        @weak workspace_filter,
        @weak environment_filter,
        @weak status,
        @strong vault,
        @strong visible_ids,
        @strong selected_id
        => move |_| {
            let count = render_list(
                &list,
                &vault.borrow(),
                search.text().as_str(),
                workspace_filter.text().as_str(),
                active_environment_filter(&environment_filter).as_deref(),
                &visible_ids,
                &selected_id,
            );
            status.set_text(&format!("{count} secret(s) shown."));
        }
    ));

    root.append(&list_panel);
    root.append(&editor);

    let window = ApplicationWindow::builder()
        .application(app)
        .title("Shush Vault")
        .default_width(980)
        .default_height(720)
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

    fs::rename(&temp_path, path)?;
    if let Some(parent) = path.parent() {
        let _ = fs::File::open(parent).and_then(|dir| dir.sync_all());
    }

    Ok(())
}

fn render_list(
    list: &ListBox,
    vault: &Vault,
    search: &str,
    workspace_filter: &str,
    environment_filter: Option<&str>,
    visible_ids: &Rc<RefCell<Vec<String>>>,
    selected_id: &Rc<RefCell<Option<String>>>,
) -> usize {
    while let Some(child) = list.first_child() {
        list.remove(&child);
    }

    let records = visible_records(vault, search, workspace_filter, environment_filter);
    let mut ids = visible_ids.borrow_mut();
    ids.clear();
    *selected_id.borrow_mut() = None;

    for record in &records {
        ids.push(record.id.to_string());

        let row = Box::new(Orientation::Vertical, 4);
        row.set_margin_top(10);
        row.set_margin_bottom(10);
        row.set_margin_start(12);
        row.set_margin_end(12);

        let primary = Label::new(Some(&format!("{}    {}", record.name, mask(&record.value))));
        primary.set_xalign(0.0);
        primary.add_css_class("heading");

        let secondary = Label::new(Some(&format!(
            "{}    {}    {}",
            record.workspace,
            record.environment,
            if record.provider.is_empty() {
                "-"
            } else {
                &record.provider
            }
        )));
        secondary.set_xalign(0.0);
        secondary.add_css_class("dim-label");

        row.append(&primary);
        row.append(&secondary);
        list.append(&row);
    }

    records.len()
}

fn visible_records<'a>(
    vault: &'a Vault,
    search: &str,
    workspace_filter: &str,
    environment_filter: Option<&str>,
) -> Vec<&'a SecretRecord> {
    let search = search.trim().to_lowercase();
    let workspace_filter = workspace_filter.trim().to_lowercase();

    vault
        .visible_records()
        .filter(|record| {
            let workspace_matches = workspace_filter.is_empty()
                || record
                    .workspace
                    .to_lowercase()
                    .contains(workspace_filter.as_str());
            let environment_matches = environment_filter
                .is_none_or(|environment| record.environment.eq_ignore_ascii_case(environment));
            let search_matches = search.is_empty()
                || record.name.to_lowercase().contains(search.as_str())
                || record.provider.to_lowercase().contains(search.as_str())
                || record.notes.to_lowercase().contains(search.as_str())
                || record.workspace.to_lowercase().contains(search.as_str())
                || record.environment.to_lowercase().contains(search.as_str());

            workspace_matches && environment_matches && search_matches
        })
        .collect()
}

fn active_environment_filter(filter: &ComboBoxText) -> Option<String> {
    match filter.active_text().map(|value| value.to_string()) {
        Some(value) if value != "All" => Some(value),
        _ => None,
    }
}

fn selected_conflict_mode(conflict: &ComboBoxText) -> ConflictMode {
    match conflict
        .active_text()
        .map(|value| value.to_string())
        .as_deref()
    {
        Some("Overwrite") => ConflictMode::Overwrite,
        Some("Rename") => ConflictMode::Rename,
        _ => ConflictMode::Skip,
    }
}

fn find_record_by_id<'a>(vault: &'a Vault, id: &str) -> Option<&'a SecretRecord> {
    vault
        .visible_records()
        .find(|record| record.id.to_string() == id)
}

fn find_record_mut_by_id<'a>(vault: &'a mut Vault, id: &str) -> Option<&'a mut SecretRecord> {
    vault
        .records
        .iter_mut()
        .find(|record| record.deleted_at.is_none() && record.id.to_string() == id)
}

fn replace_record(
    record: &mut SecretRecord,
    workspace: &str,
    name: &str,
    value: &str,
    environment: &str,
    provider: &str,
    notes: &str,
) {
    let id = record.id;
    let created_at = record.created_at;
    let mut replacement =
        SecretRecord::create(workspace, name, value, environment, provider, notes);
    replacement.id = id;
    replacement.created_at = created_at;
    *record = replacement;
}

fn import_env_records(
    vault: &mut Vault,
    content: &str,
    workspace: &str,
    environment: &str,
    provider: &str,
    conflict: ConflictMode,
) -> ImportStats {
    let mut stats = ImportStats {
        imported: 0,
        skipped: 0,
        invalid: 0,
    };

    for parsed in parse_env(content) {
        let Some(item) = parsed else {
            stats.invalid += 1;
            continue;
        };

        let exists = vault
            .find(&item.key, Some(workspace), Some(environment))
            .is_some();

        if exists && conflict == ConflictMode::Skip {
            stats.skipped += 1;
            continue;
        }

        if exists && conflict == ConflictMode::Overwrite {
            if vault.update(
                &item.key,
                Some(workspace),
                Some(environment),
                Some(&item.value),
                Some(provider),
                None,
            ) {
                stats.imported += 1;
            } else {
                stats.skipped += 1;
            }
            continue;
        }

        let key = if exists {
            unique_key(vault, workspace, environment, &item.key)
        } else {
            item.key
        };

        vault.add(SecretRecord::create(
            workspace,
            &key,
            &item.value,
            environment,
            provider,
            ".env import",
        ));
        stats.imported += 1;
    }

    stats
}

fn parse_env(content: &str) -> Vec<Option<EnvItem>> {
    content
        .lines()
        .filter_map(|line| {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                return None;
            }

            let Some((key, value)) = line.split_once('=') else {
                return Some(None);
            };

            let key = key.trim();
            if key.is_empty() {
                return Some(None);
            }

            Some(Some(EnvItem {
                key: key.to_owned(),
                value: unquote(value.trim()).to_owned(),
            }))
        })
        .collect()
}

fn unique_key(vault: &Vault, workspace: &str, environment: &str, base: &str) -> String {
    let mut suffix = 1;
    loop {
        let candidate = if suffix == 1 {
            format!("{base}_copy")
        } else {
            format!("{base}_copy{suffix}")
        };

        if vault
            .find(&candidate, Some(workspace), Some(environment))
            .is_none()
        {
            return candidate;
        }

        suffix += 1;
    }
}

fn text_view_text(view: &TextView) -> String {
    let buffer = view.buffer();
    buffer
        .text(&buffer.start_iter(), &buffer.end_iter(), true)
        .to_string()
}

fn copy_text_with_delayed_clear(text: &str) -> bool {
    let Some(display) = Display::default() else {
        return false;
    };

    display.clipboard().set_text(text);
    schedule_clipboard_clear(text.to_owned());
    true
}

fn schedule_clipboard_clear(expected_text: String) {
    glib::MainContext::default().spawn_local(async move {
        glib::timeout_future_seconds(CLIPBOARD_CLEAR_SECONDS).await;

        let Some(display) = Display::default() else {
            return;
        };

        let clipboard = display.clipboard();
        if let Ok(Some(current_text)) = clipboard.read_text_future().await {
            if current_text.as_str() == expected_text {
                clipboard.set_text("");
            }
        }
    });
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
