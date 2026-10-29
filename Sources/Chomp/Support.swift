import AppKit
import SwiftUI

// MARK: - Errors

enum ChompError: LocalizedError {
    case msg(String)
    static var needsFFmpeg: ChompError { .msg("Needs ffmpeg (brew install ffmpeg)") }
    var errorDescription: String? {
        switch self {
        case .msg(let m): return m
        }
    }
}

// MARK: - Preferences

enum Prefs {
    static var quality: Double {
        let v = UserDefaults.standard.double(forKey: "quality")
        return v == 0 ? 0.85 : v
    }
    static var beside: Bool { UserDefaults.standard.object(forKey: "outputBeside") as? Bool ?? true }
    static var haptics: Bool { UserDefaults.standard.object(forKey: "haptics") as? Bool ?? true }
    static var sounds: Bool { UserDefaults.standard.object(forKey: "sounds") as? Bool ?? true }
}

enum Accent: String, CaseIterable, Identifiable {
    case oak, lime, grape, ice, mono
    var id: String { rawValue }
    var color: Color {
        switch self {
        case .oak: return Color(red: 1.0, green: 0.83, blue: 0.36)
        case .lime: return Color(red: 0.76, green: 0.94, blue: 0.32)
        case .grape: return Color(red: 0.72, green: 0.58, blue: 1.0)
        case .ice: return Color(red: 0.48, green: 0.86, blue: 1.0)
        case .mono: return Color(white: 0.94)
        }
    }
}

// MARK: - Binaries

enum Bin {
    static var ffmpeg: String? {
        var paths = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"]
        if let bundled = Bundle.main.url(forResource: "ffmpeg", withExtension: nil)?.path {
            paths.insert(bundled, at: 0)
        }
        return paths.first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}

// MARK: - Shell

enum Shell {
    @discardableResult
    static func run(_ exe: String, _ args: [String], cwd: URL? = nil) async throws -> String {
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<String, Error>) in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: exe)
            p.arguments = args
            if let cwd { p.currentDirectoryURL = cwd }
            let errURL = Files.tempURL("log")
            FileManager.default.createFile(atPath: errURL.path, contents: nil)
            let errHandle = try? FileHandle(forWritingTo: errURL)
            p.standardError = errHandle ?? FileHandle.nullDevice
            p.standardOutput = FileHandle.nullDevice
            p.standardInput = FileHandle.nullDevice
            p.terminationHandler = { proc in
                try? errHandle?.close()
                let err = (try? String(contentsOf: errURL, encoding: .utf8)) ?? ""
                try? FileManager.default.removeItem(at: errURL)
                if proc.terminationStatus == 0 {
                    cont.resume(returning: err)
                } else {
                    let line = err.split(separator: "\n").last.map(String.init) ?? "Exit \(proc.terminationStatus)"
                    cont.resume(throwing: ChompError.msg(line))
                }
            }
            do { try p.run() } catch { cont.resume(throwing: error) }
        }
    }
}

// MARK: - Files

enum Files {
    static func outputDir(for src: URL) throws -> URL {
        if Prefs.beside { return src.deletingLastPathComponent() }
        let dir = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Chomp", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    /// "photo.heic" + ext "jpg" + suffix "small" → "photo (small).jpg", never overwriting.
    static func dest(_ src: URL, ext: String, suffix: String? = nil) throws -> URL {
        let base = src.deletingPathExtension().lastPathComponent + (suffix.map { " (\($0))" } ?? "")
        return unique(try outputDir(for: src).appendingPathComponent(base).appendingPathExtension(ext))
    }

    static func dest(named name: String, near src: URL, ext: String) throws -> URL {
        unique(try outputDir(for: src).appendingPathComponent(name).appendingPathExtension(ext))
    }

    static func unique(_ u: URL) -> URL {
        let fm = FileManager.default
        guard fm.fileExists(atPath: u.path) else { return u }
        let dir = u.deletingLastPathComponent()
        let ext = u.pathExtension
        let base = u.deletingPathExtension().lastPathComponent
        var i = 2
        while true {
            var c = dir.appendingPathComponent("\(base) \(i)")
            if !ext.isEmpty { c = c.appendingPathExtension(ext) }
            if !fm.fileExists(atPath: c.path) { return c }
            i += 1
        }
    }

    static func size(_ u: URL) -> Int64 {
        ((try? FileManager.default.attributesOfItem(atPath: u.path))?[.size] as? NSNumber)?.int64Value ?? 0
    }

    static func tempURL(_ ext: String) -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("chomp-\(UUID().uuidString).\(ext)")
    }
}

// MARK: - Masks & icons

enum Masks {
    static func rounded(_ r: CGFloat) -> NSImage {
        let s = NSSize(width: r * 2 + 1, height: r * 2 + 1)
        let img = NSImage(size: s, flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: r, yRadius: r).fill()
            return true
        }
        img.capInsets = NSEdgeInsets(top: r, left: r, bottom: r, right: r)
        img.resizingMode = .stretch
        return img
    }

    /// Blur shape behind the wheel: the disc plus the mode capsule under it.
    static func wheel() -> NSImage {
        NSImage(size: Geo.windowSize, flipped: false) { _ in
            NSColor.black.setFill()
            let c = Geo.center
            let r = Geo.outer + 8
            NSBezierPath(ovalIn: NSRect(x: c.x - r, y: c.y - r, width: r * 2, height: r * 2)).fill()
            let a = Geo.pillRect(0), b = Geo.pillRect(2)
            let rr = NSRect(x: a.minX - 5, y: a.minY - 5, width: b.maxX - a.minX + 10, height: a.height + 10)
            NSBezierPath(roundedRect: rr, xRadius: rr.height / 2, yRadius: rr.height / 2).fill()
            return true
        }
    }
}

/// Menu bar icon: a tiny closed chest with a glint of teeth, drawn pixel by pixel.
enum MenuIcon {
    static let image: NSImage = {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: true) { _ in
            func px(_ x: Int, _ y: Int, _ w: Int = 1, _ h: Int = 1) {
                NSRect(x: x, y: y, width: w, height: h).fill()
            }
            NSColor.black.setFill()
            px(3, 3, 12, 1)          // lid top
            px(2, 4, 14, 4)          // lid
            px(2, 9, 14, 6)          // body
            for x in [4, 7, 10, 13] { px(x, 8) }   // teeth across the seam
            NSColor.clear.setFill()
            NSRect(x: 8, y: 10, width: 2, height: 2).fill(using: .copy)   // keyhole
            NSRect(x: 4, y: 5, width: 1, height: 1).fill(using: .copy)    // lid glint
            return true
        }
        img.isTemplate = true
        return img
    }()
}

/// Hosting view that takes the first click, so buttons work in non-activating panels.
final class FirstMouseHostingView<V: View>: NSHostingView<V> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

// MARK: - Shapes

/// A ring segment. Angles are radians, 0 = 12 o'clock, clockwise.
struct Sector: Shape {
    var start: Double
    var end: Double
    var inner: CGFloat
    var outer: CGFloat

    var animatableData: AnimatablePair<CGFloat, CGFloat> {
        get { AnimatablePair(inner, outer) }
        set { inner = newValue.first; outer = newValue.second }
    }

    func path(in rect: CGRect) -> Path {
        let c = CGPoint(x: rect.midX, y: rect.midY)
        func pt(_ a: Double, _ r: CGFloat) -> CGPoint {
            CGPoint(x: c.x + r * CGFloat(sin(a)), y: c.y - r * CGFloat(cos(a)))
        }
        let steps = max(8, Int((end - start) * 48))
        var p = Path()
        for s in 0...steps {
            let a = start + (end - start) * Double(s) / Double(steps)
            if s == 0 { p.move(to: pt(a, outer)) } else { p.addLine(to: pt(a, outer)) }
        }
        for s in stride(from: steps, through: 0, by: -1) {
            p.addLine(to: pt(start + (end - start) * Double(s) / Double(steps), inner))
        }
        p.closeSubpath()
        return p
    }
}

// MARK: - History

struct HistoryItem: Identifiable {
    let id = UUID()
    let title: String
    let detail: String
    let outputs: [URL]
}

final class History: ObservableObject {
    static let shared = History()
    @Published var items: [HistoryItem] = []

    func add(_ title: String, _ detail: String, _ outputs: [URL]) {
        items.insert(HistoryItem(title: title, detail: detail, outputs: outputs), at: 0)
        if items.count > 12 { items.removeLast() }
    }

    func remove(_ outputs: [URL]) {
        items.removeAll { $0.outputs == outputs }
    }
}
