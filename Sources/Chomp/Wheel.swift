import AppKit
import SwiftUI

// MARK: - Geometry (shared by drawing and hit-testing)

enum Geo {
    static let size: CGFloat = CGFloat(WheelArt.grid) * WheelArt.scale   // wheel area (square), 440
    static let pillsH: CGFloat = 58       // keycap row under it
    static let outer: CGFloat = CGFloat(WheelArt.outer) * WheelArt.scale
    static let inner: CGFloat = CGFloat(WheelArt.inner) * WheelArt.scale
    static let pillW: CGFloat = 112
    static let pillH: CGFloat = 30
    static let pillGap: CGFloat = 14

    static var windowSize: NSSize { NSSize(width: size, height: size + pillsH) }
    /// Wheel centre in window coordinates (AppKit, y-up).
    static var center: NSPoint { NSPoint(x: size / 2, y: pillsH + size / 2) }

    static func pillRect(_ i: Int) -> NSRect {
        let total = pillW * 3 + pillGap * 2
        let x0 = (size - total) / 2
        return NSRect(x: x0 + CGFloat(i) * (pillW + pillGap), y: (pillsH - pillH) / 2 - 2, width: pillW, height: pillH)
    }
}

// MARK: - Model

final class WheelModel: ObservableObject {
    @Published var mode: Mode = .convert
    @Published var files: [URL] = []
    @Published var kind: Kind = .other
    @Published var count = 0
    @Published var ops: [Op] = []
    @Published var hover: Int? = nil      // segment index, -1 = centre (cancel)
    @Published var pop = 0                // 0 hidden, 1 nearly there, 2 shown: snaps in, no spring
    @Published var interactive = false
    @Published var icon: NSImage? = nil
    @Published var chompAt: Date? = nil   // set on drop; Chomp plays its bite

    /// Chomp follows the hovered segment with its eyes.
    var mood: MimicMood {
        if chompAt != nil { return .chomp }
        guard let h = hover else { return .idle }
        if h < 0 { return .closed }
        let n = max(ops.count, 1)
        let s = sin(2 * Double.pi * Double(h) / Double(n))
        return .peek(s > 0.35 ? 1 : (s < -0.35 ? -1 : 0))
    }

    func load(files: [URL], mode: Mode) {
        chompAt = nil
        self.files = files
        let s = Catalog.summary(files)
        kind = s.kind
        count = s.count
        icon = files.first.map { NSWorkspace.shared.icon(forFile: $0.path) }
        setMode(mode)
    }

    func setMode(_ m: Mode) {
        mode = m
        ops = Catalog.ops(m, kind)
        hover = nil
    }

    var label: String {
        if files.count == 1, let f = files.first, Catalog.kind(of: f) != .folder { return f.lastPathComponent }
        let n = mode == .actions ? files.count : count
        let noun = mode == .actions ? "item" : kind.noun
        return "\(n) \(noun)\(n == 1 ? "" : "s")"
    }
}

// MARK: - Controller

final class WheelController {
    static let shared = WheelController()
    let model = WheelModel()
    var onRun: ((Op, [URL]) -> Void)?

    private let panel: WheelPanel
    private var token = 0

    private init() {
        panel = WheelPanel(contentRect: NSRect(origin: .zero, size: Geo.windowSize),
                           styleMask: [.borderless, .nonactivatingPanel],
                           backing: .buffered, defer: false)
        panel.isFloatingPanel = true
        panel.level = .popUpMenu
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        let root = NSView(frame: NSRect(origin: .zero, size: Geo.windowSize))

        let host = NSHostingView(rootView: WheelRoot(model: model))
        host.frame = root.bounds
        host.autoresizingMask = [.width, .height]
        root.addSubview(host)

        let overlay = DropOverlay(frame: root.bounds)
        overlay.autoresizingMask = [.width, .height]
        root.addSubview(overlay)

        panel.contentView = root
        overlay.controller = self
        panel.onKey = { [weak self] e in self?.key(e) ?? false }
        panel.onResign = { [weak self] in self?.hide() }
    }

    /// Called when a trigger key is tapped mid-drag.
    func trigger(_ mode: Mode, files: [URL]) {
        if panel.isVisible && !model.interactive {
            switchMode(mode)
        } else {
            show(files: files, mode: mode, interactive: false, at: NSEvent.mouseLocation)
        }
    }

    func show(files: [URL], mode: Mode, interactive: Bool, at p: NSPoint) {
        guard !files.isEmpty else { return }
        token += 1
        model.interactive = interactive
        model.load(files: files, mode: mode)

        let size = Geo.windowSize
        var origin = NSPoint(x: p.x - Geo.center.x, y: p.y - Geo.center.y)
        let screen = NSScreen.screens.first { NSMouseInRect(p, $0.frame, false) } ?? NSScreen.main
        if let vf = screen?.visibleFrame {
            origin.x = min(max(origin.x, vf.minX), vf.maxX - size.width)
            origin.y = min(max(origin.y, vf.minY), vf.maxY - size.height)
        }
        panel.interactive = interactive
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        model.pop = 0
        panel.alphaValue = 1
        panel.orderFrontRegardless()
        if interactive { panel.makeKey() }
        // Two hard steps instead of a spring, like a sprite popping in.
        let t = token
        for (stage, delay) in [(1, 0.0), (2, 0.05)] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                guard let self, self.token == t else { return }
                self.model.pop = stage
            }
        }
    }

    func hide() {
        guard panel.isVisible else { return }
        token += 1
        let t = token
        model.pop = 1
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.08
            self.panel.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            guard let self, self.token == t else { return }
            self.panel.orderOut(nil)
            self.model.hover = nil
        })
    }

    /// Mouse released somewhere: give a drop on the wheel a moment to land, then go.
    func dragEnded() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) { [weak self] in
            guard let self, !self.model.interactive, self.model.chompAt == nil else { return }
            self.hide()
        }
    }

    func setHover(_ i: Int?) {
        guard model.hover != i, model.chompAt == nil else { return }
        model.hover = i
        if let i, i >= 0, Prefs.haptics {
            NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
        }
    }

    func switchMode(_ m: Mode) {
        guard model.mode != m else { return }
        model.setMode(m)
        if Prefs.haptics {
            NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
        }
    }

    func fire(_ index: Int, files: [URL]? = nil) {
        guard model.ops.indices.contains(index) else { return }
        let op = model.ops[index]
        let f: [URL]
        if let files, !files.isEmpty { f = files } else { f = model.files }
        // Let Chomp take its bite before the wheel goes away.
        model.hover = nil
        model.chompAt = Date()
        let t = token
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) { [weak self] in
            guard let self, self.token == t else { return }
            self.hide()
        }
        onRun?(op, f)
    }

    /// Keyboard, when the wheel was opened by click (Open With / menu bar).
    private func key(_ e: NSEvent) -> Bool {
        switch e.keyCode {
        case 53: hide(); return true                                   // esc
        case 48: switchMode(model.mode.next); return true              // tab
        case 123, 124:                                                 // ← →
            let n = model.ops.count
            guard n > 0 else { return true }
            let right = e.keyCode == 124
            let cur = model.hover ?? (right ? -1 : 0)
            setHover(right ? (cur + 1 + n) % n : (cur - 1 + n) % n)
            return true
        case 36, 76:                                                   // return
            if let h = model.hover, h >= 0 { fire(h) }
            return true
        default:
            if let c = e.charactersIgnoringModifiers, let d = Int(c), d >= 1, d <= model.ops.count {
                fire(d - 1)
                return true
            }
            return false
        }
    }
}

final class WheelPanel: NSPanel {
    var interactive = false
    var onKey: ((NSEvent) -> Bool)?
    var onResign: (() -> Void)?

    override var canBecomeKey: Bool { interactive }

    override func keyDown(with event: NSEvent) {
        if onKey?(event) != true { super.keyDown(with: event) }
    }

    override func resignKey() {
        super.resignKey()
        if interactive { onResign?() }
    }
}

// MARK: - Drop target / hit-testing

final class DropOverlay: NSView {
    weak var controller: WheelController?
    private var tracking: NSTrackingArea?

    enum Zone: Equatable { case segment(Int), center, pill(Mode), none }

    override init(frame: NSRect) {
        super.init(frame: frame)
        registerForDraggedTypes([.fileURL])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not used") }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let tracking { removeTrackingArea(tracking) }
        let t = NSTrackingArea(rect: bounds,
                               options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                               owner: self, userInfo: nil)
        addTrackingArea(t)
        tracking = t
    }

    func zone(_ p: NSPoint) -> Zone {
        if p.y < Geo.pillsH {
            for i in 0..<3 where Geo.pillRect(i).insetBy(dx: -3, dy: -6).contains(p) {
                return .pill(Mode(rawValue: i)!)
            }
            return .none
        }
        let c = Geo.center
        let dx = Double(p.x - c.x), dy = Double(p.y - c.y)
        let d = hypot(dx, dy)
        if d < Double(Geo.inner) { return .center }
        if d > Double(Geo.outer + 10) { return .none }
        guard let n = controller?.model.ops.count, n > 0 else { return .none }
        var a = atan2(dx, dy)                       // 0 = up, clockwise
        if a < 0 { a += 2 * .pi }
        let w = 2 * Double.pi / Double(n)
        let idx = Int(((a + w / 2).truncatingRemainder(dividingBy: 2 * .pi)) / w) % n
        return .segment(idx)
    }

    private func apply(_ z: Zone, dragging: Bool) {
        guard let c = controller else { return }
        switch z {
        case .segment(let i): c.setHover(i)
        case .center: c.setHover(-1)
        case .pill(let m):
            c.setHover(nil)
            if dragging { c.switchMode(m) }      // hover a pill mid-drag to switch, no keys needed
        case .none: c.setHover(nil)
        }
    }

    // Click mode
    override func mouseMoved(with e: NSEvent) {
        guard controller?.model.interactive == true else { return }
        apply(zone(convert(e.locationInWindow, from: nil)), dragging: false)
    }

    override func mouseExited(with event: NSEvent) {
        if controller?.model.interactive == true { controller?.setHover(nil) }
    }

    override func mouseDown(with e: NSEvent) {
        guard let c = controller, c.model.chompAt == nil else { return }
        switch zone(convert(e.locationInWindow, from: nil)) {
        case .segment(let i): c.fire(i)
        case .pill(let m): c.switchMode(m)
        case .center, .none: c.hide()
        }
    }

    // Drag mode
    private func dragOp(_ s: NSDraggingInfo) -> NSDragOperation {
        let mask = s.draggingSourceOperationMask
        for o in [NSDragOperation.copy, .generic, .link, .move] where mask.contains(o) { return o }
        return .copy
    }

    private func updated(_ s: NSDraggingInfo) -> NSDragOperation {
        let z = zone(convert(s.draggingLocation, from: nil))
        apply(z, dragging: true)
        if case .segment = z { return dragOp(s) }
        return []
    }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation { updated(sender) }
    override func draggingUpdated(_ sender: NSDraggingInfo) -> NSDragOperation { updated(sender) }
    override func draggingExited(_ sender: NSDraggingInfo?) { controller?.setHover(nil) }
    override func prepareForDragOperation(_ sender: NSDraggingInfo) -> Bool { true }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        guard case .segment(let i) = zone(convert(sender.draggingLocation, from: nil)) else { return false }
        let urls = sender.draggingPasteboard.readObjects(forClasses: [NSURL.self],
                                                         options: [.urlReadingFileURLsOnly: true]) as? [URL]
        controller?.fire(i, files: urls)
        return true
    }
}

// MARK: - Views

struct WheelRoot: View {
    @ObservedObject var model: WheelModel
    @AppStorage("skin") private var skinRaw = Skin.current.rawValue
    @AppStorage("pixelText") private var pixelText = true
    private var skin: Skin { Skin(rawValue: skinRaw) ?? .oak }

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                disc
                labels
                center
            }
            .frame(width: Geo.size, height: Geo.size)
            pills
                .frame(width: Geo.size, height: Geo.pillsH)
        }
        .scaleEffect(model.pop == 2 ? 1 : (model.pop == 1 ? 0.94 : 0.8),
                     anchor: UnitPoint(x: 0.5, y: (Geo.size / 2) / (Geo.size + Geo.pillsH)))
        .opacity(model.pop == 0 ? 0 : 1)
        .frame(width: Geo.windowSize.width, height: Geo.windowSize.height)
        .environment(\.colorScheme, .dark)
    }

    /// Snapped to the 2-point wheel grid so icons line up with the disc's pixels.
    private func point(_ a: Double, _ r: CGFloat) -> CGPoint {
        func snap(_ v: CGFloat) -> CGFloat { (v / WheelArt.scale).rounded() * WheelArt.scale }
        return CGPoint(x: snap(Geo.size / 2 + r * CGFloat(sin(a))), y: snap(Geo.size / 2 - r * CGFloat(cos(a))))
    }

    @ViewBuilder private var disc: some View {
        if let img = WheelArt.image(count: model.ops.count, hover: model.hover, skin: skin) {
            Image(decorative: img, scale: 1)
                .resizable()
                .interpolation(.none)
                .frame(width: Geo.size, height: Geo.size)
        }
    }

    private var labels: some View {
        let n = model.ops.count
        let w = n > 0 ? 2 * Double.pi / Double(n) : 0
        let r = (Geo.inner + Geo.outer) / 2 + 4
        return ZStack {
            ForEach(Array(model.ops.enumerated()), id: \.element.id) { i, op in
                SegmentLabel(op: op, hot: model.hover == i, index: i + 1, interactive: model.interactive,
                             width: min(104, CGFloat(w) * r * 0.86))
                    .position(point(Double(i) * w, r))
            }
        }
        .frame(width: Geo.size, height: Geo.size)
    }

    private var caption: String {
        if model.chompAt != nil { return "chomp." }
        if model.hover == -1 { return "nevermind" }
        if model.ops.isEmpty { return "can't eat that" }
        return model.label
    }

    private var center: some View {
        VStack(spacing: 2) {
            MimicView(mood: model.mood, since: model.chompAt ?? .distantPast, scale: 2)
                .offset(y: 2)
            Text(caption)
                .font(Fonts.label(8))
                .lineLimit(1)
                .truncationMode(.middle)
                .frame(maxWidth: Geo.inner * 2 - 40)
                .foregroundStyle(model.hover == -1 ? skin.accent : Pal.toothC)
        }
        .offset(y: -4)
    }

    private var pills: some View {
        HStack(spacing: Geo.pillGap) {
            ForEach(Mode.allCases) { m in
                Keycap(on: model.mode == m, skin: skin) {
                    HStack(spacing: 6) {
                        Text(m.key).font(.system(size: 12, weight: .heavy))
                        Text(m.title).font(Fonts.label(10))
                    }
                    .frame(width: Geo.pillW - 4, height: Geo.pillH - 4)
                }
            }
        }
        .offset(y: -2)
    }
}

struct SegmentLabel: View {
    let op: Op
    let hot: Bool
    let index: Int
    let interactive: Bool
    let width: CGFloat

    var body: some View {
        VStack(spacing: 0) {
            if let icon = op.icon {
                PixelIcon(name: icon, color: hot ? Pal.inkC : Pal.toothC)
                    .padding(.bottom, 6)
            }
            Text(op.title)
                .font(Fonts.display(op.icon == nil ? 20 : 15))
                .lineLimit(1)
                .minimumScaleFactor(0.8)
            if !op.detail.isEmpty {
                Text(op.detail)
                    .font(Fonts.body(10))
                    .opacity(hot ? 0.8 : 0.6)
                    .lineLimit(1)
                    .minimumScaleFactor(0.75)
                    .padding(.top, 4)
            }
            if interactive && index <= 9 {
                Text("\(index)")
                    .font(Fonts.label(8))
                    .opacity(0.4)
                    .padding(.top, 3)
            }
        }
        .foregroundStyle(hot ? Pal.inkC : Pal.toothC)
        .multilineTextAlignment(.center)
        .frame(width: width)
        .offset(y: op.icon == nil ? 0 : -4)
    }
}
