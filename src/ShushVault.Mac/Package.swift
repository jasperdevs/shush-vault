// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "ShushVaultMac",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "ShushVaultMac", targets: ["ShushVaultMac"])
    ],
    targets: [
        .executableTarget(
            name: "ShushVaultMac",
            path: ".",
            sources: [
                "ShushVaultApp.swift",
                "ContentView.swift",
                "SecretEditorView.swift",
                "GlassStyles.swift",
                "PlatformUnlockStore.swift",
                "VaultStore.swift"
            ]
        ),
        .testTarget(
            name: "ShushVaultMacTests",
            dependencies: ["ShushVaultMac"],
            path: "Tests"
        )
    ]
)
