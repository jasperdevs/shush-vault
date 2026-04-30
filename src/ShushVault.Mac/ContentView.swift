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

    var body: some View {
        NavigationSplitView {
            VStack(alignment: .leading, spacing: 14) {
                Text("shush vault")
                    .font(.system(size: 28, weight: .semibold))

                SecureField("Vault passphrase", text: $passphrase)
                Button("Unlock") {
                    store.unlock(passphrase: passphrase)
                    passphrase = ""
                }

                TextField("Workspace", text: $workspace)
                TextField("Key", text: $key)
                SecureField("Value", text: $value)
                Picker("Environment", selection: $environment) {
                    Text("Dev").tag("Dev")
                    Text("Staging").tag("Staging")
                    Text("Prod").tag("Prod")
                }
                TextField("Provider", text: $provider)
                TextEditor(text: $notes)
                    .frame(minHeight: 88)

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

                Text(store.status)
                    .foregroundStyle(.secondary)

                Spacer()
            }
            .padding(20)
            .navigationSplitViewColumnWidth(340)
        } detail: {
            VStack(spacing: 12) {
                TextField("Search secrets", text: $search)
                    .textFieldStyle(.roundedBorder)

                HStack {
                    Button("Edit") {
                        guard let row = selectedRow else { return }
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
                        VStack(alignment: .leading) {
                            Text(row.name)
                                .fontWeight(.semibold)
                            Text(row.maskedValue)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(row.workspace)
                        Text(row.environment)
                        Text(row.provider)
                            .foregroundStyle(.secondary)
                    }
                    .padding(.vertical, 6)
                }

                TextEditor(text: $importText)
                    .frame(minHeight: 110)
                    .overlay {
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(.secondary.opacity(0.25))
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
            .padding(20)
        }
    }

    private var filteredRows: [SecretRow] {
        if search.isEmpty {
            return store.rows
        }

        return store.rows.filter {
            $0.name.localizedCaseInsensitiveContains(search) ||
            $0.provider.localizedCaseInsensitiveContains(search) ||
            $0.notes.localizedCaseInsensitiveContains(search)
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
