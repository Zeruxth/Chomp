// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Chomp",
    platforms: [.macOS(.v13)],
    targets: [
        // Google's libwebp, built in so WebP works without ffmpeg.
        .target(
            name: "CWebP",
            path: "Sources/CWebP",
            exclude: ["COPYING", "PATENTS", "README.md"],
            cSettings: [.headerSearchPath(".")]
        ),
        .executableTarget(name: "Chomp", dependencies: ["CWebP"], path: "Sources/Chomp"),
    ]
)
