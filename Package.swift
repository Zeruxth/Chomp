// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Chomp",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "Chomp", path: "Sources/Chomp")
    ]
)
