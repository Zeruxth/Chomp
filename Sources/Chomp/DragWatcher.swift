import AppKit

/// Notices file drags anywhere on the Mac and reports a tapped ⇧ / ⌘ / ⌃ while one is in flight.
///
/// Works without Accessibility permission: it only watches global mouse-down events, then
/// polls the drag pasteboard, mouse button and modifier state until the button is released.
final class DragWatcher {
    var onTrigger: ((Mode, [URL]) -> Void)?
    var onEnd: (() -> Void)?

    private var monitor: Any?
    private var timer: Timer?
    private var startCount = 0
    private var dragging = false
    private var files: [URL] = []
    private var lastFlags: NSEvent.ModifierFlags = []
    private let triggers: NSEvent.ModifierFlags = [.shift, .command, .control]

    func start() {
        monitor = NSEvent.addGlobalMonitorForEvents(matching: .leftMouseDown) { [weak self] _ in
            self?.mouseDown()
        }
    }

    private func mouseDown() {
        startCount = NSPasteboard(name: .drag).changeCount
        dragging = false
        lastFlags = NSEvent.modifierFlags.intersection(triggers)
        timer?.invalidate()
        let t = Timer(timeInterval: 1.0 / 40, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }

    private func tick() {
        let down = NSEvent.pressedMouseButtons & 1 != 0
        if !down {
            timer?.invalidate()
            timer = nil
            if dragging {
                dragging = false
                onEnd?()
            }
            return
        }

        let pb = NSPasteboard(name: .drag)
        if !dragging, pb.changeCount != startCount {
            let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
            if urls.isEmpty {
                startCount = pb.changeCount      // text/image drag, not ours
            } else {
                dragging = true
                files = urls
                lastFlags = NSEvent.modifierFlags.intersection(triggers)
            }
        }
        guard dragging else { return }

        let flags = NSEvent.modifierFlags.intersection(triggers)
        let pressed = flags.subtracting(lastFlags)
        lastFlags = flags
        if pressed.contains(.shift) { onTrigger?(.convert, files) }
        else if pressed.contains(.command) { onTrigger?(.tools, files) }
        else if pressed.contains(.control) { onTrigger?(.actions, files) }
    }
}
