import AppKit
import SwiftUI

/// `Chomp --snapshot <folder>` saves PNGs of the wheel, toast and menu panel, then quits.
/// Handy for checking the pixel art and for screenshots on the landing page.
@MainActor
enum Snapshot {
    static func runIfAsked() {
        let args = CommandLine.arguments
        guard let i = args.firstIndex(of: "--snapshot"), i + 1 < args.count else { return }
        let dir = URL(fileURLWithPath: args[i + 1], isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let sample = [URL(fileURLWithPath: "/tmp/beach.heic")]
        let skin = args.firstIndex(of: "--skin").flatMap { $0 + 1 < args.count ? args[$0 + 1] : nil }
        let savedSkin = UserDefaults.standard.string(forKey: "skin")
        if let skin { UserDefaults.standard.set(skin, forKey: "skin") }

        for (mode, hover) in [(Mode.convert, 2), (.tools, 0), (.actions, nil)] as [(Mode, Int?)] {
            let m = WheelModel()
            m.load(files: sample, mode: mode)
            m.kind = .image
            m.ops = Catalog.ops(mode, .image)
            m.hover = hover
            m.pop = 2
            save(WheelRoot(model: m).background(Color(white: 0.55)), "wheel-\(mode.title.lowercased())", dir)
        }

        let hud = HUD.shared
        hud.phase = .done
        hud.title = "1 file → WebP"
        hud.detail = "1.8 MB saved (41%)"
        hud.outputs = sample
        save(HUDView(hud: hud).background(Color(white: 0.55)), "toast", dir)
        save(MenuPanel(), "menu", dir)
        UserDefaults.standard.set(savedSkin, forKey: "skin")
        exit(0)
    }

    private static func save<V: View>(_ v: V, _ name: String, _ dir: URL) {
        let r = ImageRenderer(content: v)
        r.scale = 2
        guard let img = r.cgImage,
              let dest = CGImageDestinationCreateWithURL(dir.appendingPathComponent(name + ".png") as CFURL,
                                                         "public.png" as CFString, 1, nil) else { return }
        CGImageDestinationAddImage(dest, img, nil)
        CGImageDestinationFinalize(dest)
    }
}
