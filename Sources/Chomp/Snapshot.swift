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

        for (name, stage) in [("working", WheelModel.Stage.working), ("done", .done(ok: true)), ("failed", .done(ok: false))] {
            let m = WheelModel()
            m.load(files: sample, mode: .convert)
            m.chompAt = .distantPast
            m.stage = stage
            m.pop = 2
            switch stage {
            case .working: m.jobTitle = "Converting to WebP…"; m.jobDetail = "beach.heic"
            case .done(true): m.jobTitle = "1 file → WebP"; m.jobDetail = "1.8 MB saved (41%)"; m.outputs = sample; m.pillHover = 0
            default: m.jobTitle = "Couldn't chew that"; m.jobDetail = "beach.heic: Couldn't save AVIF"
            }
            save(WheelRoot(model: m).background(Color(white: 0.55)), "wheel-\(name)", dir)
        }

        // Every snack at three moments of the bite: dropping in, inside the mouth, crumbs.
        let sheet = VStack(spacing: 6) {
            ForEach([2, 4, 6, 8], id: \.self) { step in
                HStack(spacing: 6) {
                    ForEach(0..<Food.all.count, id: \.self) { food in
                        Canvas { ctx, _ in Mimic.draw(Mimic.chomp(food)[step], skin: .oak, in: &ctx, scale: 3) }
                            .frame(width: 96, height: 99)
                            .background(Color(hex: Pal.hole))
                    }
                }
            }
        }
        .padding(8)
        .background(Color(white: 0.55))
        save(sheet, "foods", dir)

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

    /// `Chomp --run <op id> <file>...` runs one wheel choice from the terminal and prints what happened.
    /// e.g. `Chomp --run to.avif photo.jpg`. Used for testing conversions without dragging.
    static func runOpIfAsked() {
        let args = CommandLine.arguments
        guard let i = args.firstIndex(of: "--run"), i + 2 < args.count else { return }
        let id = args[i + 1]
        let files = args[(i + 2)...].map { URL(fileURLWithPath: $0) }
        let all = Mode.allCases.flatMap { m in [Kind.image, .video, .audio, .pdf, .document].flatMap { Catalog.ops(m, $0) } }
        guard let op = all.first(where: { $0.id == id }) else { print("No such op: \(id)"); exit(2) }
        Task {
            let r = await Engine.run(op, items: files)
            print("outputs:", r.outputs.map(\.path))
            print("failures:", r.failures)
            if let note = r.note { print("note:", note) }
            print("bytes:", r.bytesIn, "→", r.bytesOut, "skipped:", r.skipped)
            exit(r.failures.isEmpty ? 0 : 1)
        }
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
