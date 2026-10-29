import AppKit
import SwiftUI

/// Bottom-of-screen toast: progress, result, size saved, Show / Undo.
final class HUD: ObservableObject {
    enum Phase { case working, done, failed }

    static let shared = HUD()
    static let size = NSSize(width: 420, height: 68)

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
        p.hasShadow = false       // the pixel frame draws its own hard shadow
        p.hidesOnDeactivate = false
        p.isReleasedWhenClosed = false
        p.becomesKeyOnlyIfNeeded = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let host = FirstMouseHostingView(rootView: HUDView(hud: self))
        host.frame = NSRect(origin: .zero, size: Self.size)
        p.contentView = host
        return p
    }
}

struct HUDView: View {
    @ObservedObject var hud: HUD
    @AppStorage("skin") private var skinRaw = Skin.current.rawValue
    @AppStorage("pixelText") private var pixelText = true
    private var skin: Skin { Skin(rawValue: skinRaw) ?? .oak }

    var body: some View {
        HStack(spacing: 14) {
            MimicView(mood: hud.phase == .working ? .working : (hud.phase == .done ? .happy : .closed), scale: 1)
                .offset(y: 1)

            VStack(alignment: .leading, spacing: 4) {
                Text(hud.title)
                    .font(Fonts.display(15))
                    .foregroundStyle(hud.phase == .failed ? Pal.tongueC : Pal.toothC)
                    .lineLimit(1)
                if !hud.detail.isEmpty {
                    Text(hud.detail)
                        .font(Fonts.body(11.5))
                        .foregroundStyle(Pal.mutedC)
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
        .padding(.horizontal, 18)
        .frame(width: HUD.size.width - 8, height: HUD.size.height - 10)
        .pixelBox(Pal.panelC, px: 3)
        .frame(width: HUD.size.width, height: HUD.size.height, alignment: .top)
        .environment(\.colorScheme, .dark)
        .onHover { hud.hovering = $0 }
    }

    private func chip(_ t: String, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Keycap(on: false, skin: skin) {
                Text(t)
                    .font(Fonts.label(9))
                    .padding(.horizontal, 10)
                    .frame(height: 24)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
