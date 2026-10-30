import AppKit
import SwiftUI

// MARK: - Errors

enum ChompError: LocalizedError {
    case msg(String)
    static var needsFFmpeg: ChompError { .msg("This copy of Chomp has no ffmpeg: download it again from the site") }
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
    enum SaveTo: String { case beside, downloads, custom }

    /// Where results go. Falls back from the old two-way "outputBeside" setting.
    static var saveTo: SaveTo {
        let d = UserDefaults.standard
        if let raw = d.string(forKey: "saveTo"), let v = SaveTo(rawValue: raw) { return v }
        return d.object(forKey: "outputBeside") as? Bool ?? true ? .beside : .downloads
    }

    static var customFolder: URL? {
        guard let p = UserDefaults.standard.string(forKey: "customFolder"), !p.isEmpty else { return nil }
        return URL(fileURLWithPath: p, isDirectory: true)
    }
    static var haptics: Bool { UserDefaults.standard.object(forKey: "haptics") as? Bool ?? true }
    static var sounds: Bool { UserDefaults.standard.object(forKey: "sounds") as? Bool ?? true }
    static var pixelText: Bool { UserDefaults.standard.object(forKey: "pixelText") as? Bool ?? true }

    /// The size the "fit" slices aim under, in MB: 10 is Discord's limit, 25 email's.
    static let sizeLimits = [10, 25, 50]
    static var sizeLimit: Int {
        let v = UserDefaults.standard.integer(forKey: "sizeLimit")
        return sizeLimits.contains(v) ? v : 10
    }
    static var checkUpdates: Bool { UserDefaults.standard.object(forKey: "checkUpdates") as? Bool ?? true }
    static var shrinkClipboard: Bool { UserDefaults.standard.bool(forKey: "shrinkClipboard") }
    static var watchFolder: URL? {
        guard let p = UserDefaults.standard.string(forKey: "watchFolder"), !p.isEmpty else { return nil }
        return URL(fileURLWithPath: p, isDirectory: true)
    }
    static var watchRule: String { UserDefaults.standard.string(forKey: "watchRule") ?? WatchRule.heicToJpg.rawValue }

    /// Wheel slices someone switched off, as "kind:op id" (actions use "any:").
    static var hiddenSlices: Set<String> {
        get { Set(UserDefaults.standard.stringArray(forKey: "hiddenSlices") ?? []) }
        set { UserDefaults.standard.set(Array(newValue).sorted(), forKey: "hiddenSlices") }
    }
}

// MARK: - Binaries

enum Bin {
    /// The ffmpeg inside Chomp.app (releases ship one), else Homebrew's.
    static let ffmpeg: String? = {
        var paths = ["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"]
        if let bundled = Bundle.main.url(forAuxiliaryExecutable: "ffmpeg")?.path {
            paths.insert(bundled, at: 0)
        }
        return paths.first { FileManager.default.isExecutableFile(atPath: $0) }
    }()

    /// Which AV1 encoder this ffmpeg was built with, if any. Builds differ: Homebrew's has SVT-AV1 but not libaom.
    static let av1Encoder: String? = {
        guard let ff = ffmpeg else { return nil }
        let p = Process()
        p.executableURL = URL(fileURLWithPath: ff)
        p.arguments = ["-hide_banner", "-encoders"]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = FileHandle.nullDevice
        guard (try? p.run()) != nil else { return nil }
        let out = String(decoding: pipe.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        p.waitUntilExit()
        return ["libsvtav1", "libaom-av1"].first { out.contains(" \($0) ") }
    }()
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
        switch Prefs.saveTo {
        case .beside:
            return src.deletingLastPathComponent()
        case .custom:
            // A folder that's gone (deleted, drive unplugged) falls back to Downloads/Chomp.
            var isDir: ObjCBool = false
            if let dir = Prefs.customFolder,
               FileManager.default.fileExists(atPath: dir.path, isDirectory: &isDir), isDir.boolValue,
               FileManager.default.isWritableFile(atPath: dir.path) {
                return dir
            }
        case .downloads:
            break
        }
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

// MARK: - Icons

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
