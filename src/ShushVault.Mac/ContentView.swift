import SwiftUI

struct SecretRow: Identifiable {
    let id = UUID()
    let workspace: String
    let environment: String
    let name: String
    let provider: String
    let maskedValue: String
}

struct ContentView: View {
    @State private var workspace = "Default"
    @State private var key = ""
    @State private var value = ""
    @State private var environment = "Dev"
    @State private var provider = ""
    @State private var notes = ""
    @State private var search = ""
    @State private var rows: [SecretRow] = []

    var body: some View {
        NavigationSplitView {
            VStack(alignment: .leading, spacing: 14) {
                Text("shush vault")
                    .font(.system(size: 28, weight: .semibold))

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
                    rows.insert(SecretRow(
                        workspace: workspace,
                        environment: environment,
                        name: key,
                        provider: provider.isEmpty ? "-" : provider,
                        maskedValue: value.isEmpty ? "•" : String(repeating: "•", count: min(value.count, 12))
                    ), at: 0)
                    key = ""
                    value = ""
                    provider = ""
                    notes = ""
                }
                .buttonStyle(.borderedProminent)

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
                    }
                    .padding(.vertical, 6)
                }
            }
            .padding(20)
        }
    }

    private var filteredRows: [SecretRow] {
        if search.isEmpty {
            return rows
        }

        return rows.filter {
            $0.name.localizedCaseInsensitiveContains(search) ||
            $0.provider.localizedCaseInsensitiveContains(search)
        }
    }
}
