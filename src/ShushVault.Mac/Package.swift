// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "ShushVaultMac",
    platforms: [
        .macOS(.v13)
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
                "VaultStore.swift"
            ]
        )
    ]
)
