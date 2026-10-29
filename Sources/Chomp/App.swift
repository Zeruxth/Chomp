import AppKit
import SwiftUI

@main
struct ChompApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        MenuBarExtra {
            MenuPanel()
        } label: {
            Image(nsImage: MenuIcon.image)
        }
        .menuBarExtraStyle(.window)
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let watcher = DragWatcher()

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        Snapshot.runIfAsked()

        WheelController.shared.onRun = { [weak self] op, files in self?.run(op, files) }
        watcher.onTrigger = { mode, files in WheelController.shared.trigger(mode, files: files) }
        watcher.onEnd = { WheelController.shared.dragEnded() }
        watcher.start()

        if !UserDefaults.standard.bool(forKey: "onboarded") {
            UserDefaults.standard.set(true, forKey: "onboarded")
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                HUD.shared.show(.done, title: "Chomp is awake", detail: "Drag any file, then tap ⇧ ⌘ or ⌃")
            }
        }
    }

    /// Finder "Open With → Chomp", or files dropped on the app icon.
    func application(_ application: NSApplication, open urls: [URL]) {
        WheelController.shared.show(files: urls, mode: .convert, interactive: true, at: NSEvent.mouseLocation)
    }

    private func run(_ op: Op, _ files: [URL]) {
        let isConvert = op.id.hasPrefix("to.")
        let isAction = op.id.hasPrefix("act.")
        if !isAction {
            let what = files.count == 1 ? files[0].lastPathComponent : "\(files.count) items"
            HUD.shared.show(.working, title: isConvert ? "Converting to \(op.title)…" : "\(op.title)…", detail: what)
        }
        Task {
            let r = await Engine.run(op, items: files)
            await MainActor.run { self.finish(op, r) }
        }
    }

    private func finish(_ op: Op, _ r: RunResult) {
        let isConvert = op.id.hasPrefix("to.")
        let n = r.outputs.count
        var parts: [String] = []
        if let note = r.note { parts.append(note) }
        if r.note == nil, r.bytesIn > 0, r.bytesOut > 0 {
            let diff = r.bytesOut - r.bytesIn
            let f = ByteCountFormatter.string(fromByteCount: abs(diff), countStyle: .file)
            if diff <= 0 {
                let pct = Int((Double(-diff) / Double(r.bytesIn) * 100).rounded())
                parts.append("\(f) saved (\(pct)%)")
            } else {
                parts.append("+\(f)")
            }
        }
        if r.skipped > 0 { parts.append("\(r.skipped) already \(op.title)") }
        if !r.failures.isEmpty {
            parts.append(r.failures.count == 1 ? r.failures[0] : "\(r.failures.count) failed")
        }

        let ok = n > 0 || (r.failures.isEmpty && (r.note != nil || r.skipped > 0))
        let title: String
        if !ok {
            title = "Couldn't chew that"
        } else if n == 0 && r.skipped > 0 && r.note == nil {
            title = "Already \(op.title)"
        } else if op.id.hasPrefix("act.") || op.id == "doc.count" {
            title = op.title
        } else if isConvert {
            title = "\(n) file\(n == 1 ? "" : "s") → \(op.title)"
        } else {
            title = "\(op.title) · \(n) file\(n == 1 ? "" : "s")"
        }
        let detail = parts.joined(separator: " · ")

        HUD.shared.show(ok ? .done : .failed, title: title, detail: detail, outputs: r.outputs)
        if ok && !r.outputs.isEmpty { History.shared.add(title, detail, r.outputs) }
        if ok { Stats.addChomp() }
        if Prefs.sounds { NSSound(named: ok ? "Pop" : "Basso")?.play() }
    }
}
