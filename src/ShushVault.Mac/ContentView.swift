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

    var body: some View {
        NavigationSplitView {
            VStack(alignment: .leading, spacing: 14) {
                Text("shush vault")
                    .font(.system(size: 28, weight: .semibold))

                SecureField("Vault passphrase", text: $passphrase)
                Button("Unlock") {
                    store.unlock(passphrase: passphrase)
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

                Button("Save") {
                    store.add(
                        workspace: workspace,
                        name: key,
                        value: value,
                        environment: environment,
                        provider: provider,
                        notes: notes
                    )
                    key = ""
                    value = ""
                    provider = ""
                    notes = ""
                }
                .buttonStyle(.borderedProminent)

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

                List(filteredRows) { row in
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
                        Button("Delete") {
                            store.delete(row)
                        }
                    }
                    .padding(.vertical, 6)
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
}
