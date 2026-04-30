import SwiftUI

struct ContentView: View {
    @StateObject private var store = VaultStore()
    @State private var passphrase = ""
    @State private var workspace = "Default"
    @State private var key = ""
    @State private var value = ""
    @State private var environment = "Dev"
    @State private var provider = ""
    @State private var notes = ""
    @State private var search = ""
    @State private var selectedId: SecretRow.ID?
    @State private var editingId: SecretRow.ID?
    @State private var importText = ""
    @State private var conflict = "Skip"
    @State private var editorOpen = false

    var body: some View {
        VStack(spacing: 14) {
            HStack(spacing: 12) {
                Text("shush vault")
                    .font(.title2.weight(.semibold))

                Spacer()

                SecureField("Vault passphrase", text: $passphrase)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 220)
                Button("Unlock") {
                    store.unlock(passphrase: passphrase)
                    passphrase = ""
                }
            }

            HStack(spacing: 10) {
                TextField("Search secrets", text: $search)
                    .textFieldStyle(.roundedBorder)
                Button("Edit") {
                    guard let row = selectedRow else { return }
                    editorOpen = true
                    editingId = row.id
                    workspace = row.workspace
                    key = row.name
                    value = row.value
                    environment = row.environment
                    provider = row.provider
                    notes = row.notes
                }
                Button("Copy") {
                    guard let row = selectedRow else { return }
                    store.copyValue(row)
                }
                Button("Delete") {
                    guard let row = selectedRow else { return }
                    store.delete(row)
                }
                Button("Export") {
                    store.exportRows(filteredRows)
                }
            }

            List(filteredRows, selection: $selectedId) { row in
                HStack {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(row.name)
                            .fontWeight(.semibold)
                        Text(row.maskedValue)
                            .foregroundStyle(.secondary)
                            .font(.caption)
                    }
                    Spacer()
                    Text(row.workspace)
                    Text(row.environment)
                    Text(row.provider)
                        .foregroundStyle(.secondary)
                }
                .padding(.vertical, 5)
            }

            DisclosureGroup("Edit or import", isExpanded: $editorOpen) {
                Grid(horizontalSpacing: 16, verticalSpacing: 10) {
                    GridRow {
                        TextField("Workspace", text: $workspace)
                        Picker("Environment", selection: $environment) {
                            Text("Dev").tag("Dev")
                            Text("Staging").tag("Staging")
                            Text("Prod").tag("Prod")
                        }
                    }
                    GridRow {
                        TextField("Key", text: $key)
                        SecureField("Value", text: $value)
                    }
                    GridRow {
                        TextField("Provider", text: $provider)
                        TextField("Notes", text: $notes)
                    }
                    GridRow {
                        HStack {
                            Button(editingId == nil ? "Save" : "Update") {
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
                                clearEditor()
                            }
                            .buttonStyle(.borderedProminent)

                            Button("Clear") {
                                clearEditor()
                            }
                        }

                        HStack {
                            Picker("Conflict", selection: $conflict) {
                                Text("Skip").tag("Skip")
                                Text("Overwrite").tag("Overwrite")
                                Text("Rename").tag("Rename")
                            }
                            Button("Import .env") {
                                store.importEnv(
                                    content: importText,
                                    workspace: workspace,
                                    environment: environment,
                                    provider: provider,
                                    conflict: conflict
                                )
                                importText = ""
                            }
                        }
                    }
                    GridRow {
                        TextEditor(text: $importText)
                            .frame(minHeight: 90)
                            .overlay {
                                RoundedRectangle(cornerRadius: 6)
                                    .stroke(.secondary.opacity(0.25))
                            }
                            .gridCellColumns(2)
                    }
                }
                .padding(.top, 8)
            }

            Text(store.status)
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
        .padding(20)
        .frame(minWidth: 920, minHeight: 680)
    }

    private var filteredRows: [SecretRow] {
        if search.isEmpty {
            return store.rows
        }

        return store.rows.filter {
            $0.name.localizedCaseInsensitiveContains(search) ||
            $0.provider.localizedCaseInsensitiveContains(search) ||
            $0.notes.localizedCaseInsensitiveContains(search) ||
            $0.workspace.localizedCaseInsensitiveContains(search) ||
            $0.environment.localizedCaseInsensitiveContains(search)
        }
    }

    private var selectedRow: SecretRow? {
        guard let selectedId else { return nil }
        return store.rows.first { $0.id == selectedId }
    }

    private func clearEditor() {
        editingId = nil
        key = ""
        value = ""
        provider = ""
        notes = ""
    }
}
