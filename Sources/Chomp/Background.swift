import AppKit
import ImageIO

// MARK: - Watched folder

/// What happens to new files in the watched folder.
enum WatchRule: String, CaseIterable, Identifiable {
    case heicToJpg, compressImages, imagesToWebp, compressVideos, fitVideos
    var id: String { rawValue }

    var title: String {
        switch self {
        case .heicToJpg: return "HEIC photos → JPG"
        case .compressImages: return "Compress images"
        case .imagesToWebp: return "Images → WebP"
        case .compressVideos: return "Compress videos"
        case .fitVideos: return "Videos → under the size limit"
        }
    }

    var opID: String {
        switch self {
        case .heicToJpg: return "to.jpg"
        case .compressImages: return "img.compress"
        case .imagesToWebp: return "to.webp"
        case .compressVideos: return "vid.compress"
        case .fitVideos: return "vid.fit"
        }
    }

    func applies(to f: URL) -> Bool {
        let k = Catalog.kind(of: f)
        switch self {
        case .heicToJpg: return Catalog.norm(f.pathExtension) == "heic"
        case .compressImages, .imagesToWebp: return k == .image
        case .compressVideos, .fitVideos: return k == .video
        }
    }

    var op: Op? {
        let kind: Kind = [.compressVideos, .fitVideos].contains(self) ? .video : .image
        return Mode.allCases.flatMap { Catalog.allOps($0, kind) }.first { $0.id == opID }
    }
}

/// Watches one folder and runs a rule on files that land in it: iPhone photos dropped in, new screenshots…
/// Files Chomp made itself are left alone, so a rule never feeds on its own output.
@MainActor
final class FolderWatcher {
    static let shared = FolderWatcher()

    private var source: DispatchSourceFileSystemObject?
    private var known: Set<String> = []
    private var made: Set<String> = []
    private var waiting: Set<String> = []

    func restart() {
        source?.cancel()
        source = nil
        guard let dir = Prefs.watchFolder else { return }
        let fd = open(dir.path, O_EVTONLY)
        guard fd >= 0 else { return }
        known = Set(list(dir))
        let s = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename], queue: .main)
        s.setEventHandler { [weak self] in MainActor.assumeIsolated { self?.scan(dir) } }
        s.setCancelHandler { close(fd) }
        s.resume()
        source = s
    }

    private func list(_ dir: URL) -> [String] {
        (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles]))?
            .map(\.path) ?? []
    }

    private func scan(_ dir: URL) {
        let now = Set(list(dir))
        let fresh = now.subtracting(known)
        known = now
        for p in fresh where !made.contains(p) && !waiting.contains(p) {
            waiting.insert(p)
            settle(URL(fileURLWithPath: p), lastSize: -1)
        }
    }

    /// Waits until the file stops growing (a copy or download finishing), then runs the rule on it.
    private func settle(_ f: URL, lastSize: Int64) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                let size = Files.size(f)
                guard FileManager.default.fileExists(atPath: f.path) else { self.waiting.remove(f.path); return }
                if size != lastSize || size == 0 { self.settle(f, lastSize: size); return }
                self.waiting.remove(f.path)
                self.run(f)
            }
        }
    }

    private func run(_ f: URL) {
        guard !made.contains(f.path), let rule = WatchRule(rawValue: Prefs.watchRule), rule.applies(to: f), let op = rule.op else { return }
        Task {
            let r = await Engine.run(op, items: [f])
            await MainActor.run {
                self.made.formUnion(r.outputs.map(\.path))
                guard r.outputs.count > 0 || !r.failures.isEmpty else { return }
                let ok = r.failures.isEmpty
                HUD.shared.show(ok ? .done : .failed, title: ok ? "Watched folder · \(rule.title)" : "Couldn't chew that",
                                detail: ok ? f.lastPathComponent : (r.failures.first ?? ""), outputs: r.outputs)
                if ok { History.shared.add(rule.title, f.lastPathComponent, r.outputs) }
            }
        }
    }
}

// MARK: - Clipboard

/// "Copy big, paste small": a big screenshot copied to the clipboard is swapped for a JPG of it, both as a file
/// (what chat apps upload) and as the picture. Off unless switched on in More settings.
@MainActor
final class ClipboardShrinker {
    static let shared = ClipboardShrinker()
    private static let mark = NSPasteboard.PasteboardType("app.chomp.shrunk")

    private var timer: Timer?
    private var last = NSPasteboard.general.changeCount

    func start() {
        timer?.invalidate()
        let t = Timer(timeInterval: 0.5, repeats: true) { [weak self] _ in MainActor.assumeIsolated { self?.tick() } }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func tick() {
        let pb = NSPasteboard.general
        guard pb.changeCount != last else { return }
        last = pb.changeCount
        guard Prefs.shrinkClipboard, let types = pb.types, !types.contains(Self.mark), !types.contains(.fileURL),
              let data = pb.data(forType: .png) ?? pb.data(forType: .tiff), data.count > 400_000,
              let src = CGImageSourceCreateWithData(data as CFData, nil),
              let img = CGImageSourceCreateImageAtIndex(src, 0, nil),
              !Images.alpha(img) else { return }

        let dir = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("Chomp/Clipboard", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let old = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
        old.forEach { try? FileManager.default.removeItem(at: $0) }   // only the latest one is ever needed
        let f = DateFormatter()
        f.dateFormat = "yyyy-MM-dd 'at' HH.mm.ss"
        let out = dir.appendingPathComponent("Screenshot \(f.string(from: Date())).jpg")
        guard (try? Images.write(img, to: out, ext: "jpg", quality: 0.82, meta: [:])) != nil,
              let jpg = try? Data(contentsOf: out), jpg.count < data.count * 7 / 10 else { return }

        let item = NSPasteboardItem()
        item.setString(out.absoluteString, forType: .fileURL)
        item.setData(jpg, forType: NSPasteboard.PasteboardType("public.jpeg"))
        item.setData(Data(), forType: Self.mark)
        pb.clearContents()
        pb.writeObjects([item])
        last = pb.changeCount
        let b = ByteCountFormatter()
        HUD.shared.show(.done, title: "Shrunk the copied picture",
                        detail: "\(b.string(fromByteCount: Int64(data.count))) → \(b.string(fromByteCount: Int64(jpg.count)))")
    }
}

// MARK: - Updates

/// Asks GitHub once a day whether there's a newer Chomp. Sends nothing but the request itself.
@MainActor
final class Updates: ObservableObject {
    static let shared = Updates()
    struct Release { let version: String; let page: URL }
    @Published var available: Release?

    private var timer: Timer?

    func start() {
        check()
        let t = Timer(timeInterval: 24 * 3600, repeats: true) { [weak self] _ in MainActor.assumeIsolated { self?.check() } }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    func check() {
        guard Prefs.checkUpdates,
              let url = URL(string: "https://api.github.com/repos/Zeruxth/Chomp/releases/latest") else { return }
        var req = URLRequest(url: url)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        Task {
            guard let (data, _) = try? await URLSession.shared.data(for: req),
                  let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let tag = json["tag_name"] as? String,
                  let page = (json["html_url"] as? String).flatMap(URL.init(string:)) else { return }
            let latest = tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
            let mine = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
            await MainActor.run { self.available = Self.newer(latest, than: mine) ? Release(version: latest, page: page) : nil }
        }
    }

    /// "0.10.0" is newer than "0.9.2".
    static func newer(_ a: String, than b: String) -> Bool {
        let x = a.split(separator: ".").map { Int($0) ?? 0 }, y = b.split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }
}
