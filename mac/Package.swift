// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MirchiMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "MirchiMac", targets: ["MirchiMac"])],
    targets: [.executableTarget(name: "MirchiMac")]
)
