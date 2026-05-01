import SwiftUI

struct ContentView: View {
    @StateObject private var store = VaultStore()

    var body: some View {
        ZStack {
            VaultBackground()
            VaultView(store: store)
        }
    }
}

private enum EnvFilter: String, CaseIterable, Identifiable {
    case all, dev, staging, prod
    var id: String { rawValue }
    var label: String {
        switch self {
        case .all: return "All"
        case .dev: return "Dev"
        case .staging: return "Staging"
        case .prod: return "Prod"
        }
    }
    var match: String? {
        switch self {
        case .all: return nil
        case .dev: return "Dev"
        case .staging: return "Staging"
        case .prod: return "Prod"
        }
    }
}

private struct VaultView: View {
    @ObservedObject var store: VaultStore

    @State private var search = ""
    @State private var environmentFilter: EnvFilter = .all
    @State private var selectedWorkspace: String? = nil
    @State private var selectedId: SecretRow.ID?
    @State private var clipboardClear: ClipboardClearOption = .thirtySeconds
    @State private var editorRow: SecretRow?
    @State private var showingEditor = false
    @State private var showingImport = false
    @State private var showingSettings = false
    @State private var statusFlash: String?

    var body: some View {
        NavigationSplitView {
            sidebar
                .navigationSplitViewColumnWidth(min: 200, ideal: 220, max: 300)
        } detail: {
            mainPane
        }
        .navigationSplitViewStyle(.balanced)
        .frame(minWidth: 720, minHeight: 460)
        .sheet(isPresented: $showingEditor) {
            SecretEditorView(store: store, row: editorRow) {
                showingEditor = false
                editorRow = nil
            }
        }
        .sheet(isPresented: $showingImport) {
            ImportSheet(store: store) {
                showingImport = false
            }
        }
        .sheet(isPresented: $showingSettings) {
            SettingsSheet(store: store, clipboardClear: $clipboardClear) {
                showingSettings = false
            }
        }
        .toolbar {
            ToolbarItemGroup(placement: .primaryAction) {
                Button {
                    showingImport = true
                } label: {
                    Label("Import .env", systemImage: "tray.and.arrow.down")
                }
                .help("Import KEY=value lines")

                Button {
                    editorRow = nil
                    showingEditor = true
                } label: {
                    Label("New secret", systemImage: "plus.circle.fill")
                }
                .keyboardShortcut("n", modifiers: .command)
                .help("Add a new secret (⌘N)")

                Button {
                    showingSettings = true
                } label: {
                    Label("Settings", systemImage: "gearshape")
                }
            }
        }
    }

    // MARK: Sidebar

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("shush vault")
                .font(.system(size: 17, weight: .semibold, design: .rounded))
                .padding(.horizontal, 16)
                .padding(.top, 14)
                .padding(.bottom, 16)

            sidebarSection("Environment") {
                ForEach(EnvFilter.allCases) { filter in
                    sidebarRow(
                        title: filter.label,
                        icon: icon(for: filter),
                        isSelected: environmentFilter == filter,
                        accent: tint(for: filter),
                        count: environmentCount(filter)
                    ) {
                        environmentFilter = filter
                    }
                }
            }

            if !workspaces.isEmpty {
                sidebarSection("Workspaces") {
                    sidebarRow(
                        title: "All workspaces",
                        icon: "tray.full",
                        isSelected: selectedWorkspace == nil,
                        accent: .secondary,
                        count: store.rows.count
                    ) {
                        selectedWorkspace = nil
                    }

                    ForEach(workspaces, id: \.self) { workspace in
                        sidebarRow(
                            title: workspace,
                            icon: "folder",
                            isSelected: selectedWorkspace == workspace,
                            accent: .blue,
                            count: store.rows.filter { $0.workspace == workspace }.count
                        ) {
                            selectedWorkspace = workspace
                        }
                    }
                }
            }

            Spacer()

            Text("\(store.rows.count) secret\(store.rows.count == 1 ? "" : "s") in vault")
                .font(.caption2)
                .foregroundStyle(.tertiary)
                .padding(16)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    @ViewBuilder
    private func sidebarSection<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title.uppercased())
                .font(.caption2.weight(.semibold))
                .foregroundStyle(.tertiary)
                .tracking(0.5)
                .padding(.horizontal, 16)
                .padding(.top, 6)
                .padding(.bottom, 4)
            content()
        }
        .padding(.bottom, 6)
    }

    @ViewBuilder
    private func sidebarRow(
        title: String,
        icon: String,
        isSelected: Bool,
        accent: Color,
        count: Int,
        action: @escaping () -> Void
    ) -> some View {
        Button(action: action) {
            HStack(spacing: 10) {
                Image(systemName: icon)
                    .frame(width: 16)
                    .foregroundStyle(isSelected ? accent : .secondary)
                Text(title)
                    .font(.system(size: 13, weight: isSelected ? .semibold : .regular))
                    .foregroundStyle(isSelected ? .primary : .secondary)
                Spacer(minLength: 6)
                Text("\(count)")
                    .font(.caption.weight(.medium))
                    .foregroundStyle(isSelected ? accent : .tertiary)
                    .monospacedDigit()
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 7)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(isSelected ? accent.opacity(0.16) : .clear)
            )
        }
        .buttonStyle(.plain)
        .padding(.horizontal, 8)
    }

    private func icon(for filter: EnvFilter) -> String {
        switch filter {
        case .all: return "rectangle.stack"
        case .dev: return "hammer"
        case .staging: return "ladybug"
        case .prod: return "bolt.shield"
        }
    }

    private func tint(for filter: EnvFilter) -> Color {
        switch filter {
        case .all: return .secondary
        case .dev: return .blue
        case .staging: return .orange
        case .prod: return .pink
        }
    }

    private func environmentCount(_ filter: EnvFilter) -> Int {
        guard let match = filter.match else { return store.rows.count }
        return store.rows.filter { $0.environment == match }.count
    }

    private var workspaces: [String] {
        Array(Set(store.rows.map(\.workspace))).sorted()
    }

    // MARK: Main pane

    private var mainPane: some View {
        VStack(alignment: .leading, spacing: 0) {
            header

            if filteredRows.isEmpty {
                emptyState
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                secretsList
            }

            statusBar
        }
        .padding(.horizontal, 22)
        .padding(.vertical, 18)
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(alignment: .firstTextBaseline) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(currentTitle)
                        .font(.system(size: 24, weight: .semibold, design: .rounded))
                    Text("\(filteredRows.count) of \(store.rows.count) secret\(store.rows.count == 1 ? "" : "s")")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
                Spacer()
            }

            HStack(spacing: 10) {
                HStack(spacing: 8) {
                    Image(systemName: "magnifyingglass")
                        .foregroundStyle(.secondary)
                    TextField("Search keys, providers, notes…", text: $search)
                        .textFieldStyle(.plain)
                    if !search.isEmpty {
                        Button {
                            search = ""
                        } label: {
                            Image(systemName: "xmark.circle.fill")
                                .foregroundStyle(.secondary)
                        }
                        .buttonStyle(.plain)
                    }
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 9)
                .vaultGlassChrome(cornerRadius: 12)

                Button {
                    editorRow = nil
                    showingEditor = true
                } label: {
                    Label("New secret", systemImage: "plus")
                        .padding(.horizontal, 4)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
            }
        }
        .padding(.bottom, 14)
    }

    private var currentTitle: String {
        if let selectedWorkspace {
            return selectedWorkspace
        }
        if environmentFilter != .all {
            return environmentFilter.label
        }
        return "All secrets"
    }

    private var secretsList: some View {
        ScrollView {
            LazyVStack(spacing: 8) {
                ForEach(filteredRows) { row in
                    SecretRowCard(
                        row: row,
                        isSelected: selectedId == row.id,
                        onCopy: { copyRow(row) },
                        onEdit: { edit(row) },
                        onDelete: { store.delete(row) }
                    )
                    .simultaneousGesture(TapGesture().onEnded {
                        if selectedId == row.id {
                            edit(row)
                        } else {
                            selectedId = row.id
                        }
                    })
                    .contextMenu {
                        Button("Copy value") { copyRow(row) }
                        Button("Edit…") { edit(row) }
                        Divider()
                        Button("Delete", role: .destructive) { store.delete(row) }
                    }
                }
            }
            .padding(.bottom, 12)
        }
        .scrollContentBackground(.hidden)
    }

    private var emptyState: some View {
        VStack(spacing: 14) {
            Image(systemName: search.isEmpty ? "lock.shield" : "magnifyingglass")
                .font(.system(size: 42, weight: .light))
                .foregroundStyle(.secondary)
            Text(search.isEmpty ? "This view is empty" : "No matches")
                .font(.title3.weight(.medium))
            Text(search.isEmpty
                ? "Add your first secret or import a .env file to get started."
                : "Try a different search term, or clear the filters."
            )
            .font(.callout)
            .foregroundStyle(.secondary)
            .multilineTextAlignment(.center)
            .frame(maxWidth: 320)

            HStack(spacing: 10) {
                Button {
                    editorRow = nil
                    showingEditor = true
                } label: {
                    Label("New secret", systemImage: "plus")
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)

                Button {
                    showingImport = true
                } label: {
                    Label("Import .env", systemImage: "tray.and.arrow.down")
                }
                .controlSize(.large)
            }
            .padding(.top, 6)
        }
    }

    private var statusBar: some View {
        HStack(spacing: 10) {
            if !store.status.isEmpty {
                Image(systemName: store.status.lowercased().contains("could not") ? "exclamationmark.triangle.fill" : "checkmark.seal.fill")
                    .foregroundStyle(.secondary)
                Text(store.status)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Picker("Clipboard clear", selection: $clipboardClear) {
                ForEach(ClipboardClearOption.allCases) { option in
                    Text(option.title).tag(option)
                }
            }
            .pickerStyle(.segmented)
            .frame(width: 200)
            .controlSize(.small)
        }
        .padding(.top, 8)
    }

    // MARK: Filtering

    private var filteredRows: [SecretRow] {
        store.rows.filter { row in
            let workspaceMatch = selectedWorkspace.map { row.workspace == $0 } ?? true
            let envMatch = environmentFilter.match.map { row.environment == $0 } ?? true
            let searchMatch: Bool
            if search.isEmpty {
                searchMatch = true
            } else {
                searchMatch = row.name.localizedCaseInsensitiveContains(search) ||
                row.provider.localizedCaseInsensitiveContains(search) ||
                row.notes.localizedCaseInsensitiveContains(search) ||
                row.workspace.localizedCaseInsensitiveContains(search) ||
                row.environment.localizedCaseInsensitiveContains(search)
            }
            return workspaceMatch && envMatch && searchMatch
        }
    }

    // MARK: Actions

    private func edit(_ row: SecretRow) {
        editorRow = row
        showingEditor = true
    }

    private func copyRow(_ row: SecretRow) {
        store.copyValue(row, clearAfterSeconds: clipboardClear.seconds)
    }
}

struct SecretRowCard: View {
    let row: SecretRow
    let isSelected: Bool
    let onCopy: () -> Void
    let onEdit: () -> Void
    let onDelete: () -> Void

    @State private var hovering = false

    private var environmentTone: PillTag.Tone {
        switch row.environment {
        case "Prod": return .warning
        case "Staging": return .accent
        default: return .neutral
        }
    }

    var body: some View {
        HStack(spacing: 14) {
            ZStack {
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(.linearGradient(colors: [.purple.opacity(0.35), .blue.opacity(0.35)], startPoint: .topLeading, endPoint: .bottomTrailing))
                Text(String(row.name.prefix(2)).uppercased())
                    .font(.system(size: 14, weight: .bold, design: .rounded))
                    .foregroundStyle(.white)
            }
            .frame(width: 38, height: 38)

            VStack(alignment: .leading, spacing: 3) {
                Text(row.name)
                    .font(.system(.body, design: .monospaced).weight(.semibold))
                    .lineLimit(1)
                HStack(spacing: 8) {
                    Text(row.maskedValue)
                        .font(.system(.caption, design: .monospaced))
                        .foregroundStyle(.secondary)
                    if !row.provider.isEmpty {
                        Text("•")
                            .foregroundStyle(.tertiary)
                        Text(row.provider)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }

            Spacer()

            HStack(spacing: 6) {
                PillTag(text: row.workspace, tone: .neutral)
                PillTag(text: row.environment, tone: environmentTone)
            }

            HStack(spacing: 4) {
                rowAction("doc.on.doc", help: "Copy value", action: onCopy)
                rowAction("pencil", help: "Edit", action: onEdit)
                rowAction("trash", help: "Delete", tint: .red, action: onDelete)
            }
            .opacity(hovering || isSelected ? 1 : 0.45)
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
        .background(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .fill(isSelected ? Color.accentColor.opacity(0.10) : Color.white.opacity(hovering ? 0.05 : 0.025))
        )
        .overlay(
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(isSelected ? Color.accentColor.opacity(0.6) : Color.white.opacity(0.06), lineWidth: 1)
        )
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.12), value: hovering)
        .animation(.easeOut(duration: 0.12), value: isSelected)
    }

    @ViewBuilder
    private func rowAction(_ icon: String, help: String, tint: Color = .secondary, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: icon)
                .font(.system(size: 12, weight: .medium))
                .foregroundStyle(tint)
                .frame(width: 28, height: 28)
                .background(
                    RoundedRectangle(cornerRadius: 7, style: .continuous)
                        .fill(.white.opacity(0.06))
                )
        }
        .buttonStyle(.plain)
        .help(help)
    }
}
