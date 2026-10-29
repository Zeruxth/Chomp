import AppKit
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

struct MenuPanel: View {
    @ObservedObject private var history = History.shared
    @AppStorage("quality") private var quality = 0.85
    @AppStorage("outputBeside") private var beside = true
    @AppStorage("accent") private var accentRaw = Accent.oak.rawValue
    @AppStorage("haptics") private var haptics = true
    @AppStorage("sounds") private var sounds = true
    @State private var loginItem = SMAppService.mainApp.status == .enabled
    @State private var targeted = false

    private var accent: Color { (Accent(rawValue: accentRaw) ?? .oak).color }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                MimicView(mood: .idle, scale: 1)
                VStack(alignment: .leading, spacing: 0) {
                    Text("Chomp").font(.system(size: 16, weight: .bold, design: .rounded))
                    Text("Drag a file, tap ⇧ ⌘ or ⌃. I'll eat it.").font(.system(size: 11)).foregroundStyle(Color.secondary)
                }
                Spacer()
            }

            dropZone

            if Bin.ffmpeg == nil { ffmpegNote }

            if !history.items.isEmpty { recent }

            Divider()
            settings
            Divider()

            HStack {
                Text("v0.1").font(.system(size: 11)).foregroundStyle(Color.secondary.opacity(0.6))
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
                    .buttonStyle(.plain)
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(Color.secondary)
            }
        }
        .padding(16)
        .frame(width: 300)
    }

    // MARK: Sections

    private var dropZone: some View {
        Button(action: pick) {
            VStack(spacing: 6) {
                Image(systemName: targeted ? "arrow.down.circle.fill" : "plus.circle")
                    .font(.system(size: 20, weight: .medium))
                Text(targeted ? "Let go" : "Drop files here or click to pick")
                    .font(.system(size: 12, weight: .medium, design: .rounded))
            }
            .frame(maxWidth: .infinity)
            .frame(height: 78)
            .foregroundStyle(targeted ? accent : Color.secondary)
            .background(
                RoundedRectangle(cornerRadius: 14)
                    .fill(targeted ? accent.opacity(0.08) : Color.clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 14)
                    .strokeBorder(targeted ? accent : Color.secondary.opacity(0.45),
                                  style: StrokeStyle(lineWidth: 1.5, dash: [5, 4]))
            )
            .contentShape(RoundedRectangle(cornerRadius: 14))
        }
        .buttonStyle(.plain)
        .onDrop(of: [UTType.fileURL], isTargeted: $targeted) { providers in
            load(providers)
            return true
        }
    }

    private var ffmpegNote: some View {
        HStack(alignment: .top, spacing: 8) {
            Image(systemName: "exclamationmark.circle.fill").foregroundStyle(accent)
            VStack(alignment: .leading, spacing: 4) {
                Text("Video, audio, WebP and AVIF need ffmpeg")
                    .font(.system(size: 11.5, weight: .semibold))
                Button("Copy: brew install ffmpeg") {
                    let pb = NSPasteboard.general
                    pb.clearContents()
                    pb.setString("brew install ffmpeg", forType: .string)
                }
                .buttonStyle(.plain)
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(accent)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10).fill(accent.opacity(0.1)))
    }

    private var recent: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("RECENT")
                .font(.system(size: 10, weight: .bold, design: .rounded))
                .kerning(1)
                .foregroundStyle(Color.secondary)
            ForEach(history.items.prefix(5)) { item in
                Button {
                    if !item.outputs.isEmpty { NSWorkspace.shared.activateFileViewerSelecting(item.outputs) }
                } label: {
                    HStack(spacing: 8) {
                        Circle().fill(accent).frame(width: 6, height: 6)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(item.title).font(.system(size: 12, weight: .semibold)).lineLimit(1)
                            if !item.detail.isEmpty {
                                Text(item.detail).font(.system(size: 10.5)).foregroundStyle(Color.secondary).lineLimit(1)
                            }
                        }
                        Spacer()
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
    }

    private var settings: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("Quality").font(label)
                    Spacer()
                    Text("\(Int(quality * 100))")
                        .font(.system(size: 11, weight: .medium, design: .monospaced))
                        .foregroundStyle(Color.secondary)
                }
                Slider(value: $quality, in: 0.5...1.0).tint(accent).controlSize(.small)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("Save to").font(label)
                Picker("", selection: $beside) {
                    Text("Next to original").tag(true)
                    Text("Downloads/Chomp").tag(false)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }

            HStack {
                Text("Accent").font(label)
                Spacer()
                ForEach(Accent.allCases) { a in
                    Circle()
                        .fill(a.color)
                        .frame(width: 16, height: 16)
                        .overlay(
                            Circle()
                                .strokeBorder(Color.primary.opacity(accentRaw == a.rawValue ? 0.9 : 0), lineWidth: 2)
                                .padding(-3)
                        )
                        .contentShape(Circle())
                        .onTapGesture { accentRaw = a.rawValue }
                }
            }

            row("Haptics", $haptics)
            row("Sounds", $sounds)
            row("Open at login", $loginItem)
                .onChange(of: loginItem) { on in
                    do {
                        if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                    } catch {
                        loginItem = SMAppService.mainApp.status == .enabled
                    }
                }
        }
    }

    private var label: Font { .system(size: 12, weight: .medium) }

    private func row(_ title: String, _ value: Binding<Bool>) -> some View {
        HStack {
            Text(title).font(label)
            Spacer()
            Toggle("", isOn: value).labelsHidden().toggleStyle(.switch).controlSize(.mini).tint(accent)
        }
    }

    // MARK: Input

    private func pick() {
        NSApp.activate(ignoringOtherApps: true)
        let p = NSOpenPanel()
        p.allowsMultipleSelection = true
        p.canChooseDirectories = true
        p.canChooseFiles = true
        p.prompt = "Choose"
        guard p.runModal() == .OK, !p.urls.isEmpty else { return }
        let urls = p.urls
        DispatchQueue.main.async {
            let f = NSScreen.main?.visibleFrame ?? .zero
            WheelController.shared.show(files: urls, mode: .convert, interactive: true,
                                        at: NSPoint(x: f.midX, y: f.midY))
        }
    }

    private final class Box { var urls: [URL] = [] }

    private func load(_ providers: [NSItemProvider]) {
        let box = Box()
        let lock = NSLock()
        let group = DispatchGroup()
        for p in providers {
            group.enter()
            _ = p.loadObject(ofClass: URL.self) { u, _ in
                if let u {
                    lock.lock()
                    box.urls.append(u)
                    lock.unlock()
                }
                group.leave()
            }
        }
        group.notify(queue: .main) {
            WheelController.shared.show(files: box.urls, mode: .convert, interactive: true, at: NSEvent.mouseLocation)
        }
    }
}
