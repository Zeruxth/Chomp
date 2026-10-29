import AppKit
import SwiftUI

/// Bottom-of-screen toast: progress, result, size saved, Show / Undo.
final class HUD: ObservableObject {
    enum Phase { case working, done, failed }

    static let shared = HUD()
    static let size = NSSize(width: 400, height: 62)

    @Published var phase: Phase = .working
    @Published var title = ""
    @Published var detail = ""
    @Published var outputs: [URL] = []
    var hovering = false

    private var panel: NSPanel?
    private var hideItem: DispatchWorkItem?
    private var token = 0

    func show(_ phase: Phase, title: String, detail: String, outputs: [URL] = []) {
        self.phase = phase
        self.title = title
        self.detail = detail
        self.outputs = outputs
        token += 1

        let p = panel ?? makePanel()
        panel = p
        if let vf = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame {
            p.setFrameOrigin(NSPoint(x: vf.midX - Self.size.width / 2, y: vf.minY + 28))
        }
        if !p.isVisible || p.alphaValue < 1 {
            if !p.isVisible { p.alphaValue = 0 }
            p.orderFrontRegardless()
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.15
                p.animator().alphaValue = 1
            }
        }
        p.invalidateShadow()
        switch phase {
        case .working: scheduleHide(nil)
        case .done: scheduleHide(4.5)
        case .failed: scheduleHide(7)
        }
    }

    private func scheduleHide(_ secs: Double?) {
        hideItem?.cancel()
        guard let secs else { return }
        let item = DispatchWorkItem { [weak self] in self?.hideNow() }
        hideItem = item
        DispatchQueue.main.asyncAfter(deadline: .now() + secs, execute: item)
    }

    private func hideNow() {
        if hovering { scheduleHide(1.5); return }
        guard let p = panel else { return }
        let t = token
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            p.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            guard self?.token == t else { return }
            p.orderOut(nil)
        })
    }

    func reveal() {
        NSWorkspace.shared.activateFileViewerSelecting(outputs)
    }

    func undo() {
        let o = outputs
        guard !o.isEmpty else { return }
        NSWorkspace.shared.recycle(o)
        History.shared.remove(o)
        show(.failed, title: "Spat it out", detail: "Moved \(o.count) file\(o.count == 1 ? "" : "s") to the Trash")
    }

    private func makePanel() -> NSPanel {
        let p = NSPanel(contentRect: NSRect(origin: .zero, size: Self.size),
                        styleMask: [.borderless, .nonactivatingPanel],
                        backing: .buffered, defer: false)
        p.isFloatingPanel = true
        p.level = .statusBar
        p.backgroundColor = .clear
        p.isOpaque = false
        p.hasShadow = true
        p.hidesOnDeactivate = false
        p.isReleasedWhenClosed = false
        p.becomesKeyOnlyIfNeeded = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let fx = NSVisualEffectView(frame: NSRect(origin: .zero, size: Self.size))
        fx.material = .hudWindow
        fx.blendingMode = .behindWindow
        fx.state = .active
        fx.maskImage = Masks.rounded(20)

        let host = FirstMouseHostingView(rootView: HUDView(hud: self))
        host.frame = fx.bounds
        host.autoresizingMask = [.width, .height]
        fx.addSubview(host)
        p.contentView = fx
        return p
    }
}

struct HUDView: View {
    @ObservedObject var hud: HUD
    @AppStorage("accent") private var accentRaw = Accent.oak.rawValue
    private var accent: Color { (Accent(rawValue: accentRaw) ?? .oak).color }

    var body: some View {
        HStack(spacing: 12) {
            MimicView(mood: hud.phase == .working ? .working : (hud.phase == .done ? .happy : .closed), scale: 1)
                .offset(x: -2, y: 1)

            VStack(alignment: .leading, spacing: 2) {
                Text(hud.title)
                    .font(.system(size: 13, weight: .semibold, design: .rounded))
                    .lineLimit(1)
                if !hud.detail.isEmpty {
                    Text(hud.detail)
                        .font(.system(size: 11, weight: .medium, design: .rounded))
                        .foregroundStyle(Color.white.opacity(0.6))
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            }

            Spacer(minLength: 6)

            if hud.phase == .done && !hud.outputs.isEmpty {
                chip("Show") { hud.reveal() }
                chip("Undo") { hud.undo() }
            }
        }
        .padding(.horizontal, 14)
        .frame(width: HUD.size.width, height: HUD.size.height)
        .foregroundStyle(Color.white)
        .environment(\.colorScheme, .dark)
        .onHover { hud.hovering = $0 }
    }

    private func chip(_ t: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(t)
                .font(.system(size: 11.5, weight: .semibold, design: .rounded))
                .padding(.horizontal, 10)
                .frame(height: 24)
                .background(Capsule().fill(Color.white.opacity(0.12)))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }
}
