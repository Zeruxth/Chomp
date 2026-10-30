import AppKit

/// The App Store sandbox only lets Chomp write where the person has said yes: Downloads (granted by
/// entitlement), folders they dropped, and folders they picked. Picked folders are remembered as
/// security-scoped bookmarks so the yes survives a restart.
final class FolderAccess {
    static let shared = FolderAccess()
    private let key = "folderBookmarks"
    private var open: [URL] = []

    /// Call once at launch: reopens every folder the person has allowed before.
    func restore() {
        let saved = UserDefaults.standard.array(forKey: key) as? [Data] ?? []
        var keep: [Data] = []
        for data in saved {
            var stale = false
            guard let url = try? URL(resolvingBookmarkData: data, options: .withSecurityScope, relativeTo: nil, bookmarkDataIsStale: &stale),
                  url.startAccessingSecurityScopedResource() else { continue }
            open.append(url)
            keep.append(stale ? (bookmark(url) ?? data) : data)
        }
        UserDefaults.standard.set(keep, forKey: key)
    }

    func canWrite(_ dir: URL) -> Bool {
        FileManager.default.isWritableFile(atPath: dir.path)
    }

    /// Keeps access to a folder the person picked (Save to → Folder…, or an Allow prompt).
    func remember(_ url: URL) {
        guard !open.contains(url), let data = bookmark(url) else { return }
        _ = url.startAccessingSecurityScopedResource()
        open.append(url)
        var saved = UserDefaults.standard.array(forKey: key) as? [Data] ?? []
        saved.append(data)
        UserDefaults.standard.set(saved, forKey: key)
    }

    /// Asks, once, to save into `dir`. Returns true if Chomp can write there afterwards.
    @discardableResult
    func request(_ dir: URL) -> Bool {
        if canWrite(dir) { return true }
        NSApp.activate(ignoringOtherApps: true)
        let p = NSOpenPanel()
        p.canChooseFiles = false
        p.canChooseDirectories = true
        p.canCreateDirectories = true
        p.allowsMultipleSelection = false
        p.directoryURL = dir
        p.prompt = "Allow"
        p.message = "Chomp needs your OK to save into “\(dir.lastPathComponent)”. You only do this once per folder."
        guard p.runModal() == .OK, let url = p.url else { return false }
        remember(url)
        return canWrite(dir)
    }

    private func bookmark(_ url: URL) -> Data? {
        try? url.bookmarkData(options: .withSecurityScope, includingResourceValuesForKeys: nil, relativeTo: nil)
    }

    /// Folders a job will write into that might need a yes first.
    static func needed(for op: Op, files: [URL]) -> [URL] {
        switch op.id {
        case "act.desktop":
            return [FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask)[0]]
        case "act.copy", "act.path", "act.airdrop", "act.terminal", "doc.count":
            return []
        default:
            switch Prefs.saveTo {
            case .downloads: return []
            case .custom: return Prefs.customFolder.map { [$0] } ?? []
            case .beside:
                // A dropped folder is already allowed, and results land inside it.
                let loose = files.filter { Catalog.kind(of: $0) != .folder }
                return Array(Set(loose.map { $0.deletingLastPathComponent() }))
            }
        }
    }
}
