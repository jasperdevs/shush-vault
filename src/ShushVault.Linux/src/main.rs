use gtk::prelude::*;
use gtk::{glib, Application, ApplicationWindow, Box, Button, Entry, Label, ListBox, Orientation, PasswordEntry};

const APP_ID: &str = "dev.jasper.shushvault";

fn main() -> glib::ExitCode {
    let app = Application::builder().application_id(APP_ID).build();
    app.connect_activate(build_ui);
    app.run()
}

fn build_ui(app: &Application) {
    let root = Box::new(Orientation::Horizontal, 18);
    root.set_margin_top(24);
    root.set_margin_bottom(24);
    root.set_margin_start(24);
    root.set_margin_end(24);

    let editor = Box::new(Orientation::Vertical, 12);
    let title = Label::new(Some("shush vault"));
    title.add_css_class("title-1");

    let workspace = Entry::builder().placeholder_text("Workspace").text("Default").build();
    let key = Entry::builder().placeholder_text("Key").build();
    let value = PasswordEntry::builder().placeholder_text("Value").build();
    let provider = Entry::builder().placeholder_text("Provider").build();
    let save = Button::with_label("Save");

    editor.append(&title);
    editor.append(&workspace);
    editor.append(&key);
    editor.append(&value);
    editor.append(&provider);
    editor.append(&save);

    let list = ListBox::new();
    save.connect_clicked(glib::clone!(@weak key, @weak value, @weak provider, @weak list => move |_| {
        let label = Label::new(Some(&format!(
            "{}    {}    {}",
            key.text(),
            "••••••••",
            provider.text()
        )));
        label.set_xalign(0.0);
        list.prepend(&label);
        key.set_text("");
        value.set_text("");
        provider.set_text("");
    }));

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
