import SwiftUI

struct SecretEditorView: View {
    @ObservedObject var store: VaultStore
    let editingId: SecretRow.ID?
    let onClose: () -> Void

    @State private var workspace: String
    @State private var key: String
    @State private var value: String
    @State private var environment: String
    @State private var provider: String
    @State private var notes: String
    @State private var revealValue = false
    @FocusState private var keyFocused: Bool

    init(store: VaultStore, row: SecretRow?, onClose: @escaping () -> Void) {
        self.store = store
        self.editingId = row?.id
        self.onClose = onClose
        _workspace = State(initialValue: row?.workspace ?? "Default")
        _key = State(initialValue: row?.name ?? "")
        _value = State(initialValue: row?.value ?? "")
        _environment = State(initialValue: row?.environment ?? "Dev")
        _provider = State(initialValue: row?.provider ?? "")
        _notes = State(initialValue: row?.notes ?? "")
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text(editingId == nil ? "New secret" : "Edit secret")
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                Spacer()
                Button {
                    onClose()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
            }
            .padding(.horizontal, 22)
            .padding(.top, 22)
            .padding(.bottom, 14)

            Divider().opacity(0.4)

            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    field("Key") {
                        TextField("e.g. OPENAI_API_KEY", text: $key)
                            .textFieldStyle(.plain)
                            .font(.system(.body, design: .monospaced))
                            .focused($keyFocused)
                    }

                    field("Value") {
                        HStack(spacing: 8) {
                            Group {
                                if revealValue {
                                    TextField("Secret value", text: $value)
                                } else {
                                    SecureField("Secret value", text: $value)
                                }
                            }
                            .textFieldStyle(.plain)
                            .font(.system(.body, design: .monospaced))

                            Button {
                                revealValue.toggle()
                            } label: {
                                Image(systemName: revealValue ? "eye.slash" : "eye")
                                    .foregroundStyle(.secondary)
                            }
                            .buttonStyle(.plain)
                        }
                    }

                    HStack(spacing: 12) {
                        field("Workspace") {
                            TextField("Default", text: $workspace)
                                .textFieldStyle(.plain)
                        }
                        field("Environment") {
                            Picker("", selection: $environment) {
                                Text("Dev").tag("Dev")
                                Text("Staging").tag("Staging")
                                Text("Prod").tag("Prod")
                            }
                            .pickerStyle(.segmented)
                            .labelsHidden()
                        }
                    }

                    field("Provider", optional: true) {
                        TextField("OpenAI, Stripe, …", text: $provider)
                            .textFieldStyle(.plain)
                    }

                    field("Notes", optional: true) {
                        TextField("Where this is used, rotation date, …", text: $notes, axis: .vertical)
                            .textFieldStyle(.plain)
                            .lineLimit(2...4)
                    }
                }
                .padding(22)
            }

            Divider().opacity(0.4)

            HStack(spacing: 10) {
                Spacer()
                Button("Cancel", action: onClose)
                    .keyboardShortcut(.cancelAction)
                    .controlSize(.large)

                Button(action: save) {
                    Text(editingId == nil ? "Save secret" : "Update")
                        .frame(minWidth: 100)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .keyboardShortcut(.defaultAction)
                .disabled(key.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || value.isEmpty)
            }
            .padding(.horizontal, 22)
            .padding(.vertical, 14)
        }
        .frame(width: 460, height: 520)
        .vaultGlass(cornerRadius: 20)
        .onAppear { keyFocused = (editingId == nil) }
    }

    @ViewBuilder
    private func field<Content: View>(
        _ label: String,
        optional: Bool = false,
        @ViewBuilder content: () -> Content
    ) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 6) {
                Text(label)
                    .font(.caption.weight(.medium))
                    .foregroundStyle(.secondary)
                if optional {
                    Text("optional")
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
            }
            content()
                .padding(.horizontal, 12)
                .padding(.vertical, 9)
                .vaultGlassChrome(cornerRadius: 10)
        }
    }

    private func save() {
        if let editingId {
            store.update(
                id: editingId,
                workspace: workspace,
                name: key,
                value: value,
                environment: environment,
                provider: provider,
                notes: notes
            )
        } else {
            store.add(
                workspace: workspace,
                name: key,
                value: value,
                environment: environment,
                provider: provider,
                notes: notes
            )
        }
        onClose()
    }
}

struct ImportSheet: View {
    @ObservedObject var store: VaultStore
    let onClose: () -> Void

    @State private var workspace = "Default"
    @State private var environment = "Dev"
    @State private var provider = ""
    @State private var conflict = "Skip"
    @State private var importText = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Import .env")
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                Spacer()
                Button {
                    onClose()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
            }
            .padding(.horizontal, 22)
            .padding(.top, 22)
            .padding(.bottom, 14)

            Divider().opacity(0.4)

            VStack(alignment: .leading, spacing: 14) {
                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Workspace")
                            .font(.caption.weight(.medium))
                            .foregroundStyle(.secondary)
                        TextField("Default", text: $workspace)
                            .textFieldStyle(.plain)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 9)
                            .vaultGlassChrome(cornerRadius: 10)
                    }

                    VStack(alignment: .leading, spacing: 6) {
                        Text("Environment")
                            .font(.caption.weight(.medium))
                            .foregroundStyle(.secondary)
                        Picker("", selection: $environment) {
                            Text("Dev").tag("Dev")
                            Text("Staging").tag("Staging")
                            Text("Prod").tag("Prod")
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                    }
                }

                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 6) {
                        Text("Provider").font(.caption.weight(.medium)).foregroundStyle(.secondary)
                        TextField("optional", text: $provider)
                            .textFieldStyle(.plain)
                            .padding(.horizontal, 12)
                            .padding(.vertical, 9)
                            .vaultGlassChrome(cornerRadius: 10)
                    }

                    VStack(alignment: .leading, spacing: 6) {
                        Text("On conflict").font(.caption.weight(.medium)).foregroundStyle(.secondary)
                        Picker("", selection: $conflict) {
                            Text("Skip").tag("Skip")
                            Text("Overwrite").tag("Overwrite")
                            Text("Rename").tag("Rename")
                        }
                        .pickerStyle(.segmented)
                        .labelsHidden()
                    }
                }

                VStack(alignment: .leading, spacing: 6) {
                    Text("KEY=value lines")
                        .font(.caption.weight(.medium))
                        .foregroundStyle(.secondary)
                    TextEditor(text: $importText)
                        .font(.system(.body, design: .monospaced))
                        .frame(minHeight: 160)
                        .scrollContentBackground(.hidden)
                        .padding(8)
                        .vaultGlassChrome(cornerRadius: 10)
                }
            }
            .padding(22)

            Divider().opacity(0.4)

            HStack {
                Text("Existing secrets default to Skip. Set Overwrite to replace, Rename to keep both.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Cancel", action: onClose)
                    .controlSize(.large)
                Button(action: importNow) {
                    Text("Import")
                        .frame(minWidth: 90)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(importText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            .padding(.horizontal, 22)
            .padding(.vertical, 14)
        }
        .frame(width: 540, height: 520)
        .vaultGlass(cornerRadius: 20)
    }

    private func importNow() {
        store.importEnv(
            content: importText,
            workspace: workspace,
            environment: environment,
            provider: provider,
            conflict: conflict
        )
        onClose()
    }
}

struct SettingsSheet: View {
    @ObservedObject var store: VaultStore
    @Binding var clipboardClear: ClipboardClearOption
    let onClose: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Settings")
                    .font(.system(size: 18, weight: .semibold, design: .rounded))
                Spacer()
                Button {
                    onClose()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.title3)
                        .foregroundStyle(.secondary)
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
            }
            .padding(.horizontal, 22)
            .padding(.top, 22)
            .padding(.bottom, 14)

            Divider().opacity(0.4)

            VStack(alignment: .leading, spacing: 22) {
                section("Clipboard") {
                    Picker("Auto-clear copied values", selection: $clipboardClear) {
                        ForEach(ClipboardClearOption.allCases) { option in
                            Text(option.title).tag(option)
                        }
                    }
                    .pickerStyle(.segmented)
                    .labelsHidden()

                    Text("Copies and .env exports clear from the clipboard automatically.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }

            }
            .padding(22)

            Spacer()
        }
        .frame(width: 460, height: 540)
        .vaultGlass(cornerRadius: 20)
    }

    @ViewBuilder
    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title.uppercased())
                .font(.caption2.weight(.semibold))
                .foregroundStyle(.tertiary)
                .tracking(0.6)
            content()
        }
    }
}

enum ClipboardClearOption: String, CaseIterable, Identifiable {
    case fifteenSeconds, thirtySeconds, sixtySeconds, never

    var id: String { rawValue }

    var title: String {
        switch self {
        case .fifteenSeconds: return "15s"
        case .thirtySeconds: return "30s"
        case .sixtySeconds: return "60s"
        case .never: return "Never"
        }
    }

    var seconds: Int? {
        switch self {
        case .fifteenSeconds: return 15
        case .thirtySeconds: return 30
        case .sixtySeconds: return 60
        case .never: return nil
        }
    }
}
