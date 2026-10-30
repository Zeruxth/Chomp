import AppKit
import AVFoundation
import AVKit
import SwiftUI

/// The Trim slice: a small window to pick the part of a video to keep and, if you like, a shape to crop it to.
/// The wheel steps aside while it's open; the result shows in the bottom toast like any other job.
@MainActor
enum Editor {
    private static var window: NSWindow?

    /// Shows the editor for one video and waits. nil means it was closed without saving.
    static func ask(_ src: URL) async -> EditPlan? {
        WheelController.shared.hide()
        window?.close()
        return await withCheckedContinuation { (cont: CheckedContinuation<EditPlan?, Never>) in
            var done = false
            let finish: (EditPlan?) -> Void = { plan in
                guard !done else { return }
                done = true
                cont.resume(returning: plan)
                window?.close()
                window = nil
            }
            let model = EditorModel(url: src)
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 640, height: 560),
                             styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
            w.title = src.lastPathComponent
            w.titlebarAppearsTransparent = true
            w.isReleasedWhenClosed = false
            w.backgroundColor = NSColor(Pal.panelC)
            w.contentView = NSHostingView(rootView: EditorView(model: model, onDone: finish))
            w.center()
            NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
                MainActor.assumeIsolated {
                    model.player.pause()
                    finish(nil)
                }
            }
            window = w
            NSApp.activate(ignoringOtherApps: true)
            w.makeKeyAndOrderFront(nil)
        }
    }
}

@MainActor
final class EditorModel: ObservableObject {
    let player: AVPlayer
    @Published var duration: Double = 0
    @Published var start: Double = 0
    @Published var end: Double = 0
    @Published var aspect: EditPlan.Aspect = .original
    @Published var offset: Double = 0.5
    @Published var size = CGSize(width: 16, height: 9)

    init(url: URL) {
        player = AVPlayer(url: url)
        Task {
            let asset = AVURLAsset(url: url)
            let d = (try? await asset.load(.duration).seconds) ?? 0
            if let track = try? await asset.loadTracks(withMediaType: .video).first,
               let (natural, transform) = try? await track.load(.naturalSize, .preferredTransform) {
                let s = natural.applying(transform)
                size = CGSize(width: abs(s.width), height: abs(s.height))
            }
            duration = d.isFinite ? d : 0
            end = duration
        }
    }

    func seek(_ t: Double) {
        player.seek(to: CMTime(seconds: t, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }
}

struct EditorView: View {
    @ObservedObject var model: EditorModel
    let onDone: (EditPlan?) -> Void
    @AppStorage("skin") private var skinRaw = Skin.current.rawValue
    private var skin: Skin { Skin(rawValue: skinRaw) ?? .oak }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            preview
            TrimBar(model: model, accent: skin.accent)
            HStack(spacing: 10) {
                Text(L("Shape")).font(Fonts.display(14, .regular)).foregroundStyle(Pal.mutedC)
                ForEach(EditPlan.Aspect.allCases) { a in
                    Button { withAnimation(.easeOut(duration: 0.18)) { model.aspect = a; model.offset = 0.5 } } label: {
                        Keycap(on: model.aspect == a, skin: skin) {
                            Text(a.title).font(Fonts.label(10)).padding(.horizontal, 12).frame(height: 28)
                        }
                    }
                    .buttonStyle(.plain)
                }
                Spacer()
                Button { onDone(nil) } label: {
                    Keycap(on: false, skin: skin) { Text(L("Cancel")).font(Fonts.label(10)).padding(.horizontal, 14).frame(height: 30) }
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.cancelAction)
                Button {
                    model.player.pause()
                    onDone(EditPlan(start: model.start, end: model.end, aspect: model.aspect, offset: model.offset))
                } label: {
                    Keycap(on: true, skin: skin) { Text(L("Save")).font(Fonts.label(10)).padding(.horizontal, 18).frame(height: 30) }
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.defaultAction)
                .disabled(model.duration == 0)
            }
        }
        .padding(EdgeInsets(top: 34, leading: 20, bottom: 20, trailing: 20))
        .frame(width: 640, height: 560)
        .background(Pal.panelC)
        .foregroundStyle(Pal.toothC)
        .environment(\.colorScheme, .dark)
    }

    /// The video, with everything outside the crop dimmed. Drag the bright box to move it.
    private var preview: some View {
        GeometryReader { geo in
            let fit = fitted(model.size, in: geo.size)
            ZStack(alignment: .topLeading) {
                VideoPlayer(player: model.player)
                if let r = model.aspect.ratio {
                    let box = cropBox(r, in: fit)
                    Path { p in
                        p.addRect(fit)
                        p.addRect(box)
                    }
                    .fill(Color.black.opacity(0.6), style: FillStyle(eoFill: true))
                    .allowsHitTesting(false)
                    Rectangle()
                        .strokeBorder(skin.accent, lineWidth: 2)
                        .frame(width: box.width, height: box.height)
                        .offset(x: box.minX, y: box.minY)
                        .contentShape(Rectangle())
                        .gesture(DragGesture(coordinateSpace: .named("preview")).onChanged { g in
                            let roomX = fit.width - box.width, roomY = fit.height - box.height
                            let room = max(roomX, roomY)
                            guard room > 1 else { return }
                            let along = roomX > roomY ? g.location.x - fit.minX - box.width / 2 : g.location.y - fit.minY - box.height / 2
                            model.offset = min(1, max(0, along / room))
                        })
                }
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .coordinateSpace(name: "preview")
        }
        .frame(height: 360)
        .background(Color.black)
        .clipShape(Rectangle())
    }

    private func fitted(_ s: CGSize, in area: CGSize) -> CGRect {
        guard s.width > 0, s.height > 0 else { return CGRect(origin: .zero, size: area) }
        let k = min(area.width / s.width, area.height / s.height)
        let w = s.width * k, h = s.height * k
        return CGRect(x: (area.width - w) / 2, y: (area.height - h) / 2, width: w, height: h)
    }

    private func cropBox(_ r: Double, in f: CGRect) -> CGRect {
        let w = min(f.width, f.height * r), h = min(f.height, f.width / r)
        return CGRect(x: f.minX + (f.width - w) * model.offset, y: f.minY + (f.height - h) * model.offset, width: w, height: h)
    }
}

/// Two handles on a track: the part between them is kept. Dragging one shows that frame.
struct TrimBar: View {
    @ObservedObject var model: EditorModel
    let accent: Color

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            GeometryReader { geo in
                let w = geo.size.width
                let d = max(model.duration, 0.001)
                let x0 = w * model.start / d, x1 = w * model.end / d
                ZStack(alignment: .leading) {
                    Rectangle().fill(Pal.raisedC).frame(height: 14)
                    Rectangle().fill(accent.opacity(0.55)).frame(width: max(0, x1 - x0), height: 14).offset(x: x0)
                    handle.offset(x: x0 - 5)
                        .gesture(DragGesture(coordinateSpace: .named("track")).onChanged { g in
                            model.start = min(max(0, g.location.x / w * d), model.end - 0.1)
                            model.seek(model.start)
                        })
                    handle.offset(x: x1 - 5)
                        .gesture(DragGesture(coordinateSpace: .named("track")).onChanged { g in
                            model.end = max(min(d, g.location.x / w * d), model.start + 0.1)
                            model.seek(model.end)
                        })
                }
                .frame(height: 30)
                .coordinateSpace(name: "track")
            }
            .frame(height: 30)
            HStack {
                Text(clock(model.start)).font(.system(size: 11, design: .monospaced)).foregroundStyle(Pal.mutedC)
                Spacer()
                Text(L("Keeping {0}", clock(model.end - model.start))).font(Fonts.body(12)).foregroundStyle(Pal.toothC)
                Spacer()
                Text(clock(model.end)).font(.system(size: 11, design: .monospaced)).foregroundStyle(Pal.mutedC)
            }
        }
    }

    private var handle: some View {
        Rectangle().fill(Pal.toothC).frame(width: 10, height: 30)
            .overlay(Rectangle().strokeBorder(Pal.inkC, lineWidth: 2))
            .contentShape(Rectangle().inset(by: -8))
    }

    private func clock(_ t: Double) -> String {
        let s = max(0, t)
        return String(format: "%d:%04.1f", Int(s) / 60, s.truncatingRemainder(dividingBy: 60))
    }
}
