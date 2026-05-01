use std::cell::RefCell;
use std::fs;
use std::path::PathBuf;
use std::rc::Rc;

use gtk::gdk::Display;
use gtk::prelude::*;
use gtk::{
    glib, Align, Application, ApplicationWindow, Box, Button, CssProvider, Entry, HeaderBar, Label,
    ListBox, ListBoxRow, Orientation, PasswordEntry, ScrolledWindow, SearchEntry, SelectionMode,
    Stack, TextView, ToggleButton, Window,
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

#[derive(Default, Clone)]
struct Filters {
    search: String,
    environment: Option<String>,
}

struct AppState {
    vault: RefCell<Vault>,
    passphrase: RefCell<String>,
    filters: RefCell<Filters>,
    clipboard_seconds: RefCell<Option<u32>>,
    vault_path: PathBuf,
}

type Refresh = Rc<dyn Fn()>;

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_startup(|_| install_css());
    app.connect_activate(build_ui);
    app.run()
}

fn install_css() {
    let provider = CssProvider::new();
    provider.load_from_data(
        r#"
        .vault-hero { font-size: 22pt; font-weight: 700; }
        .vault-subtitle { color: alpha(@theme_fg_color, 0.6); }
        .vault-card {
            background-color: alpha(@theme_fg_color, 0.04);
            border: 1px solid alpha(@theme_fg_color, 0.08);
            border-radius: 12px;
            padding: 10px 12px;
        }
        .vault-card:hover { background-color: alpha(@theme_fg_color, 0.08); }
        .vault-avatar {
            min-width: 36px; min-height: 36px;
            border-radius: 9px;
            font-weight: 700;
            background-color: @accent_bg_color;
            color: @accent_fg_color;
        }
        .vault-pill {
            border-radius: 999px;
            padding: 2px 10px;
            font-size: 10pt;
            font-weight: 600;
            background-color: alpha(@theme_fg_color, 0.1);
        }
        .vault-pill.dev { background-color: alpha(#4FC3F7, 0.25); color: #4FC3F7; }
        .vault-pill.staging { background-color: alpha(#FFC107, 0.25); color: #FFC107; }
        .vault-pill.prod { background-color: alpha(#FF6B6B, 0.30); color: #FF6B6B; }
        .vault-key { font-family: "Cascadia Code", "JetBrains Mono", monospace; font-weight: 600; }
        .vault-value { font-family: "Cascadia Code", "JetBrains Mono", monospace; color: alpha(@theme_fg_color, 0.65); }
        .vault-meta { color: alpha(@theme_fg_color, 0.55); font-size: 10pt; }
        .vault-status { color: alpha(@theme_fg_color, 0.55); font-size: 10pt; }
        .vault-empty-icon {
            min-width: 76px; min-height: 76px;
            border-radius: 22px;
            background-color: alpha(@accent_bg_color, 0.3);
            color: @accent_fg_color;
            font-size: 28pt;
        }
        list.vault-list { background: transparent; }
        list.vault-list > row { background: transparent; padding: 0; }
        list.vault-list > row:hover { background: transparent; }
        "#,
    );
    if let Some(display) = Display::default() {
        gtk::style_context_add_provider_for_display(
            &display,
            &provider,
            gtk::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
    }
}

fn build_ui(app: &Application) {
    let state = Rc::new(AppState {
        vault: RefCell::new(Vault::default()),
        passphrase: RefCell::new(String::new()),
        filters: RefCell::new(Filters::default()),
        clipboard_seconds: RefCell::new(Some(CLIPBOARD_CLEAR_SECONDS)),
        vault_path: default_vault_path(),
    });

    let stack = Stack::new();
    stack.set_transition_type(gtk::StackTransitionType::Crossfade);

    let lock = build_lock_view(&state);
    stack.add_named(&lock.root, Some("locked"));

    let unlocked = build_unlocked_view();
    stack.add_named(&unlocked.root, Some("unlocked"));

    let header = HeaderBar::new();
    header.set_show_title_buttons(true);
    let title = Label::new(Some("Shush Vault"));
    title.add_css_class("title");
    header.set_title_widget(Some(&title));

    let new_button = Button::from_icon_name("list-add-symbolic");
    new_button.set_tooltip_text(Some("New secret"));
    new_button.add_css_class("suggested-action");
    let import_button = Button::from_icon_name("document-import-symbolic");
    import_button.set_tooltip_text(Some("Import .env"));
    let settings_button = Button::from_icon_name("emblem-system-symbolic");
    settings_button.set_tooltip_text(Some("Settings"));
    let lock_button = Button::from_icon_name("system-lock-screen-symbolic");
    lock_button.set_tooltip_text(Some("Lock vault"));

    header.pack_start(&new_button);
    header.pack_start(&import_button);
    header.pack_end(&settings_button);
    header.pack_end(&lock_button);

    let window = ApplicationWindow::builder()
        .application(app)
        .title("Shush Vault")
        .default_width(940)
        .default_height(680)
        .child(&stack)
        .build();
    window.set_titlebar(Some(&header));

    // Build the refresh closure with self-reference so row buttons can re-render.
    let refresh_slot: Rc<RefCell<Option<Refresh>>> = Rc::new(RefCell::new(None));
    let refresh: Refresh = {
        let state = state.clone();
        let list = unlocked.list.clone();
        let stack_view = unlocked.list_stack.clone();
        let empty_title = unlocked.empty_title.clone();
        let empty_subtitle = unlocked.empty_subtitle.clone();
        let status = unlocked.status.clone();
        let window = window.clone();
        let refresh_slot = refresh_slot.clone();
        Rc::new(move || {
            let visible = visible_records(&state.vault.borrow(), &state.filters.borrow())
                .into_iter()
                .cloned()
                .collect::<Vec<_>>();
            let total = state.vault.borrow().visible_records().count();
            render_list(
                &list,
                &visible,
                &state,
                &refresh_slot,
                &status,
                &window,
            );
            if visible.is_empty() {
                update_empty_state(
                    &empty_title,
                    &empty_subtitle,
                    &state.filters.borrow(),
                    total,
                );
                stack_view.set_visible_child_name("empty");
            } else {
                stack_view.set_visible_child_name("list");
            }
            status.set_text(&match visible.len() {
                0 => String::new(),
                1 => "1 secret shown.".to_string(),
                n => format!("{n} secrets shown."),
            });
        })
    };
    *refresh_slot.borrow_mut() = Some(refresh.clone());

    // Show/hide header buttons based on stack
    let header_visibility: Rc<dyn Fn(bool)> = {
        let new_button = new_button.clone();
        let import_button = import_button.clone();
        let lock_button = lock_button.clone();
        Rc::new(move |unlocked: bool| {
            new_button.set_visible(unlocked);
            import_button.set_visible(unlocked);
            lock_button.set_visible(unlocked);
        })
    };
    header_visibility(false);
    {
        let header_visibility = header_visibility.clone();
        stack.connect_visible_child_notify(move |s| {
            header_visibility(s.visible_child_name().as_deref() == Some("unlocked"));
        });
    }

    // Lock-screen unlock action
    let try_unlock: Rc<dyn Fn()> = {
        let state = state.clone();
        let stack = stack.clone();
        let refresh = refresh.clone();
        let passphrase = lock.passphrase.clone();
        let status = lock.status.clone();
        Rc::new(move || {
            let entered = passphrase.text().to_string();
            if entered.trim().is_empty() {
                status.set_text("Enter a passphrase.");
                return;
            }
            match load_vault(&state.vault_path, &entered) {
                Ok(loaded) => {
                    *state.vault.borrow_mut() = loaded;
                    *state.passphrase.borrow_mut() = entered;
                    passphrase.set_text("");
                    status.set_text("");
                    stack.set_visible_child_name("unlocked");
                    refresh();
                }
                Err(_) => status.set_text("That passphrase did not unlock the vault."),
            }
        })
    };
    {
        let try_unlock = try_unlock.clone();
        lock.passphrase.connect_activate(move |_| try_unlock());
    }
    {
        let try_unlock = try_unlock.clone();
        lock.unlock.connect_clicked(move |_| try_unlock());
    }

    // Header button handlers
    {
        let state = state.clone();
        let refresh = refresh.clone();
        let window = window.clone();
        let status = unlocked.status.clone();
        new_button.connect_clicked(move |_| {
            open_secret_dialog(&window, &state, None, &refresh, &status);
        });
    }
    {
        let state = state.clone();
        let refresh = refresh.clone();
        let window = window.clone();
        let status = unlocked.status.clone();
        import_button.connect_clicked(move |_| {
            open_import_dialog(&window, &state, &refresh, &status);
        });
    }
    {
        let state = state.clone();
        let window = window.clone();
        let status = unlocked.status.clone();
        settings_button.connect_clicked(move |_| {
            open_settings_dialog(&window, &state, &status);
        });
    }
    {
        let state = state.clone();
        let stack = stack.clone();
        let refresh = refresh.clone();
        let status = unlocked.status.clone();
        lock_button.connect_clicked(move |_| {
            *state.vault.borrow_mut() = Vault::default();
            state.passphrase.borrow_mut().clear();
            state.filters.borrow_mut().search.clear();
            stack.set_visible_child_name("locked");
            status.set_text("Locked.");
            refresh();
        });
    }

    // Search & filter handlers
    {
        let state = state.clone();
        let refresh = refresh.clone();
        unlocked.search.connect_search_changed(move |entry| {
            state.filters.borrow_mut().search = entry.text().to_string();
            refresh();
        });
    }
    let env_buttons = unlocked.env_buttons.clone();
    for button in &env_buttons {
        let state = state.clone();
        let refresh = refresh.clone();
        let group = env_buttons.clone();
        button.connect_toggled(move |btn| {
            if !btn.is_active() {
                if !group.iter().any(|b| b.is_active()) {
                    btn.set_active(true);
                }
                return;
            }
            for other in &group {
                if other != btn {
                    other.set_active(false);
                }
            }
            let label = btn.label().map(|s| s.to_string()).unwrap_or_else(|| "All".into());
            state.filters.borrow_mut().environment =
                if label == "All" { None } else { Some(label) };
            refresh();
        });
    }

    // Empty-state quick add buttons
    {
        let state = state.clone();
        let refresh = refresh.clone();
        let window = window.clone();
        let status = unlocked.status.clone();
        unlocked.empty_new.connect_clicked(move |_| {
            open_secret_dialog(&window, &state, None, &refresh, &status);
        });
    }
    {
        let state = state.clone();
        let refresh = refresh.clone();
        let window = window.clone();
        let status = unlocked.status.clone();
        unlocked.empty_import.connect_clicked(move |_| {
            open_import_dialog(&window, &state, &refresh, &status);
        });
    }

    stack.set_visible_child_name("locked");
    window.present();
    lock.passphrase.grab_focus();
}

struct UnlockedView {
    root: Box,
    list: ListBox,
    list_stack: Stack,
    empty_title: Label,
    empty_subtitle: Label,
    empty_new: Button,
    empty_import: Button,
    status: Label,
    search: SearchEntry,
    env_buttons: Vec<ToggleButton>,
}

fn build_unlocked_view() -> UnlockedView {
    let root = Box::new(Orientation::Vertical, 14);
    root.set_margin_top(18);
    root.set_margin_bottom(18);
    root.set_margin_start(22);
    root.set_margin_end(22);

    let search = SearchEntry::new();
    search.set_placeholder_text(Some("Search keys, providers, notes…"));
    search.set_hexpand(true);
    let search_row = Box::new(Orientation::Horizontal, 8);
    search_row.append(&search);
    root.append(&search_row);

    let filter_row = Box::new(Orientation::Horizontal, 6);
    filter_row.set_halign(Align::Start);
    let env_buttons: Vec<ToggleButton> = ["All", "Dev", "Staging", "Prod"]
        .iter()
        .map(|name| {
            let button = ToggleButton::with_label(name);
            button.add_css_class("pill");
            if *name == "All" {
                button.set_active(true);
            }
            filter_row.append(&button);
            button
        })
        .collect();
    root.append(&filter_row);

    let list_stack = Stack::new();
    list_stack.set_vexpand(true);

    let list = ListBox::new();
    list.set_selection_mode(SelectionMode::None);
    list.add_css_class("vault-list");
    let scroll = ScrolledWindow::new();
    scroll.set_vexpand(true);
    scroll.set_hscrollbar_policy(gtk::PolicyType::Never);
    scroll.set_child(Some(&list));
    list_stack.add_named(&scroll, Some("list"));

    let empty = Box::new(Orientation::Vertical, 12);
    empty.set_valign(Align::Center);
    empty.set_halign(Align::Center);
    let empty_icon = Label::new(Some("\u{1F510}"));
    empty_icon.add_css_class("vault-empty-icon");
    empty_icon.set_halign(Align::Center);
    let empty_title = Label::new(Some("No secrets yet"));
    empty_title.add_css_class("vault-hero");
    let empty_subtitle = Label::new(Some(
        "Add your first API key or import a .env file. Everything is encrypted on disk with AES-256-GCM.",
    ));
    empty_subtitle.add_css_class("vault-subtitle");
    empty_subtitle.set_wrap(true);
    empty_subtitle.set_halign(Align::Center);
    empty_subtitle.set_max_width_chars(48);

    let empty_actions = Box::new(Orientation::Horizontal, 8);
    empty_actions.set_halign(Align::Center);
    let empty_new = Button::with_label("New secret");
    empty_new.add_css_class("suggested-action");
    empty_new.add_css_class("pill");
    let empty_import = Button::with_label("Import .env");
    empty_import.add_css_class("pill");
    empty_actions.append(&empty_new);
    empty_actions.append(&empty_import);

    empty.append(&empty_icon);
    empty.append(&empty_title);
    empty.append(&empty_subtitle);
    empty.append(&empty_actions);
    list_stack.add_named(&empty, Some("empty"));
    list_stack.set_visible_child_name("empty");

    root.append(&list_stack);

    let status = Label::new(None);
    status.add_css_class("vault-status");
    status.set_halign(Align::Start);
    status.set_wrap(true);
    root.append(&status);

    UnlockedView {
        root,
        list,
        list_stack,
        empty_title,
        empty_subtitle,
        empty_new,
        empty_import,
        status,
        search,
        env_buttons,
    }
}

struct LockView {
    root: Box,
    passphrase: PasswordEntry,
    unlock: Button,
    status: Label,
}

fn build_lock_view(state: &Rc<AppState>) -> LockView {
    let outer = Box::new(Orientation::Vertical, 0);
    outer.set_valign(Align::Center);
    outer.set_halign(Align::Center);
    outer.set_margin_top(60);
    outer.set_margin_bottom(60);
    outer.set_margin_start(40);
    outer.set_margin_end(40);

    let panel = Box::new(Orientation::Vertical, 14);
    panel.set_width_request(380);
    panel.add_css_class("vault-card");
    panel.set_margin_top(20);
    panel.set_margin_bottom(20);
    panel.set_margin_start(20);
    panel.set_margin_end(20);

    let avatar = Label::new(Some("\u{1F512}"));
    avatar.add_css_class("vault-empty-icon");
    avatar.set_halign(Align::Center);

    let title = Label::new(Some("Welcome back"));
    title.add_css_class("vault-hero");
    title.set_halign(Align::Center);

    let subtitle = Label::new(Some("Enter your vault passphrase to continue."));
    subtitle.add_css_class("vault-subtitle");
    subtitle.set_halign(Align::Center);
    subtitle.set_wrap(true);

    let passphrase = PasswordEntry::builder()
        .placeholder_text("Vault passphrase")
        .show_peek_icon(true)
        .build();
    passphrase.set_hexpand(true);

    let unlock = Button::with_label("Unlock");
    unlock.add_css_class("suggested-action");
    unlock.add_css_class("pill");

    let status = Label::new(None);
    status.add_css_class("vault-status");
    status.set_halign(Align::Center);
    status.set_wrap(true);

    let path_label = Label::new(Some(&format!("Vault stored at {}", state.vault_path.display())));
    path_label.add_css_class("vault-meta");
    path_label.set_halign(Align::Center);
    path_label.set_wrap(true);

    panel.append(&avatar);
    panel.append(&title);
    panel.append(&subtitle);
    panel.append(&passphrase);
    panel.append(&unlock);
    panel.append(&status);
    panel.append(&path_label);

    outer.append(&panel);
    LockView { root: outer, passphrase, unlock, status }
}

fn render_list(
    list: &ListBox,
    records: &[SecretRecord],
    state: &Rc<AppState>,
    refresh_slot: &Rc<RefCell<Option<Refresh>>>,
    status: &Label,
    window: &ApplicationWindow,
) {
    while let Some(child) = list.first_child() {
        list.remove(&child);
    }

    for record in records {
        let row = ListBoxRow::new();
        row.set_activatable(false);
        row.set_selectable(false);

        let card = Box::new(Orientation::Horizontal, 12);
        card.add_css_class("vault-card");
        card.set_margin_top(4);
        card.set_margin_bottom(4);

        let avatar = Label::new(Some(&initials(&record.name)));
        avatar.add_css_class("vault-avatar");
        avatar.set_valign(Align::Center);
        avatar.set_halign(Align::Center);

        let info = Box::new(Orientation::Vertical, 2);
        info.set_hexpand(true);
        info.set_valign(Align::Center);

        let key_label = Label::new(Some(&record.name));
        key_label.add_css_class("vault-key");
        key_label.set_xalign(0.0);
        key_label.set_ellipsize(gtk::pango::EllipsizeMode::End);

        let detail_row = Box::new(Orientation::Horizontal, 8);
        let masked = Label::new(Some(&mask(&record.value)));
        masked.add_css_class("vault-value");
        detail_row.append(&masked);
        if !record.provider.is_empty() {
            let provider = Label::new(Some(&format!("· {}", record.provider)));
            provider.add_css_class("vault-meta");
            detail_row.append(&provider);
        }
        info.append(&key_label);
        info.append(&detail_row);

        let pills = Box::new(Orientation::Horizontal, 6);
        pills.set_valign(Align::Center);
        let workspace_pill = Label::new(Some(&record.workspace));
        workspace_pill.add_css_class("vault-pill");
        let env_pill = Label::new(Some(&record.environment));
        env_pill.add_css_class("vault-pill");
        match record.environment.as_str() {
            "Dev" => env_pill.add_css_class("dev"),
            "Staging" => env_pill.add_css_class("staging"),
            "Prod" => env_pill.add_css_class("prod"),
            _ => {}
        }
        pills.append(&workspace_pill);
        pills.append(&env_pill);

        let actions = Box::new(Orientation::Horizontal, 2);
        actions.set_valign(Align::Center);

        let copy_btn = Button::from_icon_name("edit-copy-symbolic");
        copy_btn.set_tooltip_text(Some("Copy value"));
        copy_btn.add_css_class("flat");
        let edit_btn = Button::from_icon_name("document-edit-symbolic");
        edit_btn.set_tooltip_text(Some("Edit"));
        edit_btn.add_css_class("flat");
        let delete_btn = Button::from_icon_name("user-trash-symbolic");
        delete_btn.set_tooltip_text(Some("Delete"));
        delete_btn.add_css_class("flat");

        actions.append(&copy_btn);
        actions.append(&edit_btn);
        actions.append(&delete_btn);

        let id = record.id.to_string();
        let name = record.name.clone();

        {
            let id = id.clone();
            let state = state.clone();
            let status = status.clone();
            copy_btn.connect_clicked(move |_| copy_record(&state, &id, &status));
        }
        {
            let id = id.clone();
            let state = state.clone();
            let refresh_slot = refresh_slot.clone();
            let status = status.clone();
            let window = window.clone();
            edit_btn.connect_clicked(move |_| {
                let refresh = refresh_slot.borrow().clone();
                if let Some(refresh) = refresh {
                    open_secret_dialog(&window, &state, Some(id.clone()), &refresh, &status);
                }
            });
        }
        {
            let id = id.clone();
            let name = name.clone();
            let state = state.clone();
            let refresh_slot = refresh_slot.clone();
            let status = status.clone();
            delete_btn.connect_clicked(move |_| {
                let passphrase = state.passphrase.borrow().clone();
                if passphrase.is_empty() {
                    status.set_text("Vault is locked.");
                    return;
                }
                let removed = {
                    let mut vault = state.vault.borrow_mut();
                    let before = vault.records.len();
                    vault.records.retain(|r| r.id.to_string() != id);
                    before != vault.records.len()
                };
                if !removed {
                    status.set_text("Selected secret no longer exists.");
                    return;
                }
                if save_vault(&state.vault_path, &passphrase, &state.vault.borrow()).is_err() {
                    status.set_text("Could not save encrypted vault.");
                    return;
                }
                status.set_text(&format!("Deleted {}.", name));
                if let Some(refresh) = refresh_slot.borrow().clone() {
                    refresh();
                }
            });
        }

        card.append(&avatar);
        card.append(&info);
        card.append(&pills);
        card.append(&actions);
        row.set_child(Some(&card));
        list.append(&row);
    }
}

fn update_empty_state(title: &Label, subtitle: &Label, filters: &Filters, total: usize) {
    if total == 0 {
        title.set_text("No secrets yet");
        subtitle.set_text(
            "Add your first API key or import a .env file. Everything is encrypted on disk with AES-256-GCM.",
        );
        return;
    }
    if !filters.search.trim().is_empty() {
        title.set_text("No matches");
        subtitle.set_text(&format!(
            "Nothing matches “{}” in the current filters.",
            filters.search.trim()
        ));
        return;
    }
    title.set_text("Nothing in this environment");
    subtitle.set_text("Switch to a different environment, or add a new secret here.");
}

fn visible_records<'a>(vault: &'a Vault, filters: &Filters) -> Vec<&'a SecretRecord> {
    let search = filters.search.trim().to_lowercase();
    let env = filters.environment.clone();
    vault
        .visible_records()
        .filter(|record| {
            let env_matches = env
                .as_deref()
                .is_none_or(|environment| record.environment.eq_ignore_ascii_case(environment));
            let search_matches = search.is_empty()
                || record.name.to_lowercase().contains(search.as_str())
                || record.provider.to_lowercase().contains(search.as_str())
                || record.notes.to_lowercase().contains(search.as_str())
                || record.workspace.to_lowercase().contains(search.as_str())
                || record.environment.to_lowercase().contains(search.as_str());
            env_matches && search_matches
        })
        .collect()
}

fn copy_record(state: &Rc<AppState>, id: &str, status: &Label) {
    let vault = state.vault.borrow();
    let Some(record) = find_record_by_id(&vault, id) else {
        status.set_text("Selected secret no longer exists.");
        return;
    };
    let clear_seconds = *state.clipboard_seconds.borrow();
    if copy_text_with_delayed_clear(&record.value, clear_seconds) {
        status.set_text(&clipboard_status(
            &format!("Copied {}.", record.name),
            clear_seconds,
        ));
    } else {
        status.set_text("Clipboard is not available.");
    }
}

fn open_secret_dialog(
    parent: &ApplicationWindow,
    state: &Rc<AppState>,
    editing_id: Option<String>,
    refresh: &Refresh,
    status: &Label,
) {
    let dialog = Window::builder()
        .transient_for(parent)
        .modal(true)
        .resizable(false)
        .default_width(420)
        .build();
    dialog.set_title(Some(if editing_id.is_some() { "Edit secret" } else { "New secret" }));

    let outer = Box::new(Orientation::Vertical, 14);
    outer.set_margin_top(18);
    outer.set_margin_bottom(18);
    outer.set_margin_start(18);
    outer.set_margin_end(18);

    let workspace = labelled_entry("Workspace", "Default");
    let key = labelled_entry("Key", "");
    let value_box = Box::new(Orientation::Vertical, 4);
    let value_label = Label::new(Some("Value"));
    value_label.set_xalign(0.0);
    value_label.add_css_class("vault-meta");
    let value_entry = PasswordEntry::builder()
        .placeholder_text("Secret value")
        .show_peek_icon(true)
        .build();
    value_box.append(&value_label);
    value_box.append(&value_entry);

    let environment = environment_picker("Dev");
    let provider = labelled_entry("Provider (optional)", "");
    let notes = labelled_entry("Notes (optional)", "");

    let actions = Box::new(Orientation::Horizontal, 8);
    actions.set_halign(Align::End);
    let cancel = Button::with_label("Cancel");
    let save = Button::with_label(if editing_id.is_some() { "Save changes" } else { "Save secret" });
    save.add_css_class("suggested-action");
    actions.append(&cancel);
    actions.append(&save);

    outer.append(&workspace.row);
    outer.append(&key.row);
    outer.append(&value_box);
    outer.append(&environment.row);
    outer.append(&provider.row);
    outer.append(&notes.row);
    outer.append(&actions);

    if let Some(id) = &editing_id {
        let vault = state.vault.borrow();
        if let Some(record) = find_record_by_id(&vault, id) {
            workspace.entry.set_text(&record.workspace);
            key.entry.set_text(&record.name);
            value_entry.set_text(&record.value);
            for button in &environment.buttons {
                let active = button.label().map(|l| l.to_string()).as_deref()
                    == Some(record.environment.as_str());
                button.set_active(active);
            }
            provider.entry.set_text(&record.provider);
            notes.entry.set_text(&record.notes);
        }
    }

    cancel.connect_clicked(glib::clone!(@weak dialog => move |_| dialog.close()));

    {
        let dialog = dialog.clone();
        let state = state.clone();
        let refresh = refresh.clone();
        let status = status.clone();
        let workspace_entry = workspace.entry.clone();
        let key_entry = key.entry.clone();
        let value_entry = value_entry.clone();
        let provider_entry = provider.entry.clone();
        let notes_entry = notes.entry.clone();
        let env_buttons = environment.buttons.clone();
        let editing_id = editing_id.clone();
        save.connect_clicked(move |_| {
            let key_text = key_entry.text().to_string();
            let value_text = value_entry.text().to_string();
            if key_text.trim().is_empty() || value_text.is_empty() {
                status.set_text("Key and value are required.");
                return;
            }

            let env_text = env_buttons
                .iter()
                .find(|b| b.is_active())
                .and_then(|b| b.label())
                .map(|l| l.to_string())
                .unwrap_or_else(|| "Dev".into());

            let passphrase = state.passphrase.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Vault is locked.");
                return;
            }

            if let Some(id) = &editing_id {
                let mut vault = state.vault.borrow_mut();
                let Some(record) = find_record_mut_by_id(&mut vault, id) else {
                    status.set_text("Selected secret no longer exists.");
                    return;
                };
                replace_record(
                    record,
                    &workspace_entry.text(),
                    &key_text,
                    &value_text,
                    &env_text,
                    &provider_entry.text(),
                    &notes_entry.text(),
                );
            } else {
                state.vault.borrow_mut().add(SecretRecord::create(
                    &workspace_entry.text(),
                    &key_text,
                    &value_text,
                    &env_text,
                    &provider_entry.text(),
                    &notes_entry.text(),
                ));
            }

            if save_vault(&state.vault_path, &passphrase, &state.vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            status.set_text(if editing_id.is_some() { "Secret updated." } else { "Secret saved." });
            refresh();
            dialog.close();
        });
    }

    dialog.set_child(Some(&outer));
    dialog.present();
    key.entry.grab_focus();
}

fn open_import_dialog(
    parent: &ApplicationWindow,
    state: &Rc<AppState>,
    refresh: &Refresh,
    status: &Label,
) {
    let dialog = Window::builder()
        .transient_for(parent)
        .modal(true)
        .default_width(620)
        .default_height(520)
        .build();
    dialog.set_title(Some("Import .env"));

    let outer = Box::new(Orientation::Vertical, 12);
    outer.set_margin_top(18);
    outer.set_margin_bottom(18);
    outer.set_margin_start(18);
    outer.set_margin_end(18);

    let intro = Label::new(Some("Paste KEY=value lines. Existing matches respect the conflict mode."));
    intro.add_css_class("vault-subtitle");
    intro.set_xalign(0.0);
    intro.set_wrap(true);

    let workspace = labelled_entry("Workspace", "Default");
    let environment = environment_picker("Dev");
    let provider = labelled_entry("Provider applied to imports (optional)", "");

    let conflict_row = Box::new(Orientation::Horizontal, 6);
    let conflict_label = Label::new(Some("On conflict:"));
    conflict_label.set_xalign(0.0);
    conflict_label.add_css_class("vault-meta");
    conflict_row.append(&conflict_label);
    let conflict_buttons: Vec<ToggleButton> = ["Skip", "Overwrite", "Rename"]
        .iter()
        .map(|name| {
            let button = ToggleButton::with_label(name);
            button.add_css_class("pill");
            if *name == "Skip" {
                button.set_active(true);
            }
            conflict_row.append(&button);
            button
        })
        .collect();
    {
        let group = conflict_buttons.clone();
        for button in &conflict_buttons {
            let group = group.clone();
            button.connect_toggled(move |btn| {
                if !btn.is_active() {
                    if !group.iter().any(|b| b.is_active()) {
                        btn.set_active(true);
                    }
                    return;
                }
                for other in &group {
                    if other != btn {
                        other.set_active(false);
                    }
                }
            });
        }
    }

    let import_view = TextView::new();
    import_view.set_monospace(true);
    import_view.set_wrap_mode(gtk::WrapMode::None);
    let import_scroll = ScrolledWindow::new();
    import_scroll.set_min_content_height(180);
    import_scroll.set_vexpand(true);
    import_scroll.set_child(Some(&import_view));

    let actions = Box::new(Orientation::Horizontal, 8);
    actions.set_halign(Align::End);
    let paste = Button::with_label("Paste clipboard");
    let cancel = Button::with_label("Cancel");
    let import = Button::with_label("Import");
    import.add_css_class("suggested-action");
    actions.append(&paste);
    actions.append(&cancel);
    actions.append(&import);

    outer.append(&intro);
    outer.append(&workspace.row);
    outer.append(&environment.row);
    outer.append(&provider.row);
    outer.append(&conflict_row);
    outer.append(&import_scroll);
    outer.append(&actions);

    {
        let import_view = import_view.clone();
        let status = status.clone();
        paste.connect_clicked(move |_| {
            let Some(display) = Display::default() else {
                status.set_text("Clipboard is not available.");
                return;
            };
            let clipboard = display.clipboard();
            let import_view = import_view.clone();
            let status = status.clone();
            glib::MainContext::default().spawn_local(async move {
                match clipboard.read_text_future().await {
                    Ok(Some(text)) => {
                        import_view.buffer().set_text(text.as_str());
                        status.set_text("Pasted clipboard text into .env import.");
                    }
                    Ok(None) => status.set_text("Clipboard does not contain text."),
                    Err(_) => status.set_text("Could not read clipboard text."),
                }
            });
        });
    }

    cancel.connect_clicked(glib::clone!(@weak dialog => move |_| dialog.close()));

    {
        let dialog = dialog.clone();
        let state = state.clone();
        let refresh = refresh.clone();
        let status = status.clone();
        let import_view = import_view.clone();
        let workspace_entry = workspace.entry.clone();
        let provider_entry = provider.entry.clone();
        let env_buttons = environment.buttons.clone();
        let conflict_buttons = conflict_buttons.clone();
        import.connect_clicked(move |_| {
            let passphrase = state.passphrase.borrow().clone();
            if passphrase.is_empty() {
                status.set_text("Vault is locked.");
                return;
            }
            let content = text_view_text(&import_view);
            if content.trim().is_empty() {
                status.set_text("Paste KEY=value lines first.");
                return;
            }
            let env_text = env_buttons
                .iter()
                .find(|b| b.is_active())
                .and_then(|b| b.label())
                .map(|l| l.to_string())
                .unwrap_or_else(|| "Dev".into());
            let conflict = conflict_buttons
                .iter()
                .find(|b| b.is_active())
                .and_then(|b| b.label())
                .map(|l| l.to_string());
            let conflict = match conflict.as_deref() {
                Some("Overwrite") => ConflictMode::Overwrite,
                Some("Rename") => ConflictMode::Rename,
                _ => ConflictMode::Skip,
            };

            let stats = {
                let mut vault = state.vault.borrow_mut();
                import_env_records(
                    &mut vault,
                    &content,
                    &workspace_entry.text(),
                    &env_text,
                    &provider_entry.text(),
                    conflict,
                )
            };

            if stats.imported > 0 && save_vault(&state.vault_path, &passphrase, &state.vault.borrow()).is_err() {
                status.set_text("Could not save encrypted vault.");
                return;
            }

            status.set_text(&format!(
                "Imported {}, skipped {}, invalid {}.",
                stats.imported, stats.skipped, stats.invalid
            ));
            refresh();
            dialog.close();
        });
    }

    dialog.set_child(Some(&outer));
    dialog.present();
}

fn open_settings_dialog(parent: &ApplicationWindow, state: &Rc<AppState>, status: &Label) {
    let dialog = Window::builder()
        .transient_for(parent)
        .modal(true)
        .resizable(false)
        .default_width(380)
        .build();
    dialog.set_title(Some("Settings"));

    let outer = Box::new(Orientation::Vertical, 14);
    outer.set_margin_top(18);
    outer.set_margin_bottom(18);
    outer.set_margin_start(18);
    outer.set_margin_end(18);

    let title = Label::new(Some("Clipboard auto-clear"));
    title.set_xalign(0.0);
    title.add_css_class("vault-key");
    let subtitle = Label::new(Some("Applies to copied secret values and exports."));
    subtitle.set_xalign(0.0);
    subtitle.add_css_class("vault-subtitle");
    subtitle.set_wrap(true);

    let pills = Box::new(Orientation::Horizontal, 6);
    let options: [(Option<u32>, &str); 4] = [(Some(15), "15s"), (Some(30), "30s"), (Some(60), "60s"), (None, "Never")];
    let pairs: Vec<(Option<u32>, ToggleButton)> = options
        .iter()
        .map(|(seconds, label)| {
            let button = ToggleButton::with_label(label);
            button.add_css_class("pill");
            if *seconds == *state.clipboard_seconds.borrow() {
                button.set_active(true);
            }
            pills.append(&button);
            (*seconds, button)
        })
        .collect();

    {
        let group: Vec<ToggleButton> = pairs.iter().map(|(_, b)| b.clone()).collect();
        for (seconds, button) in &pairs {
            let group = group.clone();
            let state = state.clone();
            let status = status.clone();
            let seconds = *seconds;
            button.connect_toggled(move |btn| {
                if !btn.is_active() {
                    if !group.iter().any(|b| b.is_active()) {
                        btn.set_active(true);
                    }
                    return;
                }
                for other in &group {
                    if other != btn {
                        other.set_active(false);
                    }
                }
                *state.clipboard_seconds.borrow_mut() = seconds;
                let message = match seconds {
                    Some(value) => format!("Clipboard clears after {value}s."),
                    None => "Clipboard auto-clear is off.".to_string(),
                };
                status.set_text(&message);
            });
        }
    }

    let path_title = Label::new(Some("Vault file"));
    path_title.set_xalign(0.0);
    path_title.add_css_class("vault-key");
    let path_value = Label::new(Some(&state.vault_path.display().to_string()));
    path_value.set_xalign(0.0);
    path_value.add_css_class("vault-meta");
    path_value.set_wrap(true);
    path_value.set_selectable(true);

    let close = Button::with_label("Done");
    close.add_css_class("suggested-action");
    close.set_halign(Align::End);
    close.connect_clicked(glib::clone!(@weak dialog => move |_| dialog.close()));

    outer.append(&title);
    outer.append(&subtitle);
    outer.append(&pills);
    outer.append(&path_title);
    outer.append(&path_value);
    outer.append(&close);

    dialog.set_child(Some(&outer));
    dialog.present();
}

struct LabelledEntry {
    row: Box,
    entry: Entry,
}

fn labelled_entry(label_text: &str, default: &str) -> LabelledEntry {
    let row = Box::new(Orientation::Vertical, 4);
    let label = Label::new(Some(label_text));
    label.set_xalign(0.0);
    label.add_css_class("vault-meta");
    let entry = Entry::builder().text(default).build();
    row.append(&label);
    row.append(&entry);
    LabelledEntry { row, entry }
}

struct EnvironmentPicker {
    row: Box,
    buttons: Vec<ToggleButton>,
}

fn environment_picker(default: &str) -> EnvironmentPicker {
    let row = Box::new(Orientation::Vertical, 4);
    let label = Label::new(Some("Environment"));
    label.set_xalign(0.0);
    label.add_css_class("vault-meta");

    let pills = Box::new(Orientation::Horizontal, 6);
    let buttons: Vec<ToggleButton> = ["Dev", "Staging", "Prod"]
        .iter()
        .map(|name| {
            let button = ToggleButton::with_label(name);
            button.add_css_class("pill");
            if *name == default {
                button.set_active(true);
            }
            pills.append(&button);
            button
        })
        .collect();
    {
        let group = buttons.clone();
        for button in &buttons {
            let group = group.clone();
            button.connect_toggled(move |btn| {
                if !btn.is_active() {
                    if !group.iter().any(|b| b.is_active()) {
                        btn.set_active(true);
                    }
                    return;
                }
                for other in &group {
                    if other != btn {
                        other.set_active(false);
                    }
                }
            });
        }
    }

    row.append(&label);
    row.append(&pills);
    EnvironmentPicker { row, buttons }
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

fn find_record_by_id<'a>(vault: &'a Vault, id: &str) -> Option<&'a SecretRecord> {
    vault.visible_records().find(|record| record.id.to_string() == id)
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
    let mut stats = ImportStats { imported: 0, skipped: 0, invalid: 0 };

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
        if vault.find(&candidate, Some(workspace), Some(environment)).is_none() {
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

fn copy_text_with_delayed_clear(text: &str, clear_seconds: Option<u32>) -> bool {
    let Some(display) = Display::default() else {
        return false;
    };
    display.clipboard().set_text(text);
    if let Some(clear_seconds) = clear_seconds {
        schedule_clipboard_clear(text.to_owned(), clear_seconds);
    }
    true
}

fn schedule_clipboard_clear(expected_text: String, clear_seconds: u32) {
    glib::MainContext::default().spawn_local(async move {
        glib::timeout_future_seconds(clear_seconds).await;
        let Some(display) = Display::default() else { return; };
        let clipboard = display.clipboard();
        if let Ok(Some(current_text)) = clipboard.read_text_future().await {
            if current_text.as_str() == expected_text {
                clipboard.set_text("");
            }
        }
    });
}

fn clipboard_status(prefix: &str, clear_seconds: Option<u32>) -> String {
    match clear_seconds {
        Some(seconds) => format!("{prefix} Clipboard clears in {seconds}s."),
        None => format!("{prefix} Clipboard auto-clear is off."),
    }
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

fn mask(value: &str) -> String {
    if value.len() <= 4 {
        "•".repeat(value.len().max(1))
    } else {
        format!("{}{}", "•".repeat(8), &value[value.len() - 4..])
    }
}

fn initials(name: &str) -> String {
    let trimmed = name.trim();
    if trimmed.is_empty() {
        return "·".to_string();
    }
    let parts: Vec<&str> = trimmed
        .split(|c: char| c == '_' || c == '-' || c == '.' || c.is_whitespace())
        .filter(|part| !part.is_empty())
        .collect();
    if parts.len() >= 2 {
        let first = parts[0].chars().next().unwrap_or('·');
        let second = parts[1].chars().next().unwrap_or('·');
        return format!("{}{}", first.to_ascii_uppercase(), second.to_ascii_uppercase());
    }
    let head = parts.into_iter().next().unwrap_or(trimmed);
    let mut chars = head.chars();
    match (chars.next(), chars.next()) {
        (Some(a), Some(b)) => format!("{}{}", a.to_ascii_uppercase(), b.to_ascii_lowercase()),
        (Some(a), None) => a.to_ascii_uppercase().to_string(),
        _ => "·".to_string(),
    }
}
