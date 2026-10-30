import AppKit
import ServiceManagement
import SwiftUI
import UniformTypeIdentifiers

struct MenuPanel: View {
    @ObservedObject private var history = History.shared
    @AppStorage("quality") private var quality = 0.85
    @AppStorage("saveTo") private var saveTo = Prefs.saveTo.rawValue
    @AppStorage("customFolder") private var customFolder = ""
    @State private var lastSaveTo = Prefs.saveTo.rawValue
    @AppStorage("skin") private var skinRaw = Skin.current.rawValue
    @AppStorage("pixelText") private var pixelText = true
    @AppStorage("haptics") private var haptics = true
    @AppStorage("sounds") private var sounds = true
    @State private var loginItem = SMAppService.mainApp.status == .enabled
    @State private var targeted = false

    private var skin: Skin { Skin(rawValue: skinRaw) ?? .oak }
    private var accent: Color { skin.accent }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                MimicView(mood: .idle, scale: 2)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Chomp").font(Fonts.display(22, .bold)).foregroundStyle(accent)
                    Text("Drag a file, tap ⇧ ⌘ or ⌃. I'll eat it.").font(Fonts.body(11)).foregroundStyle(Pal.mutedC)
                }
                Spacer()
            }

            dropZone


            if !history.items.isEmpty { recent }

            Divider()
            settings
            Divider()

            HStack {
                Text("v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev") · \(Stats.chomps) chomps")
                    .font(Fonts.body(11)).foregroundStyle(Pal.mutedC.opacity(0.7))
                Spacer()
                Button("Quit") { NSApp.terminate(nil) }
                    .buttonStyle(.plain)
                    .font(Fonts.label(10))
                    .foregroundStyle(Pal.mutedC)
            }
        }
        .padding(18)
        .frame(width: 320)
        .foregroundStyle(Pal.toothC)
        .background(Pal.panelC)
        .environment(\.colorScheme, .dark)
    }

    // MARK: Sections

    private var dropZone: some View {
        Button(action: pick) {
            VStack(spacing: 6) {
                PixelIcon(name: targeted ? "combine" : "copy", color: targeted ? accent : Pal.mutedC)
                Text(targeted ? "Let go" : "Drop files here or click to pick")
                    .font(Fonts.display(14, .regular))
            }
            .frame(maxWidth: .infinity)
            .frame(height: 84)
            .foregroundStyle(targeted ? accent : Pal.mutedC)
            .background(Rectangle().fill(targeted ? accent.opacity(0.1) : Color.clear))
            .overlay(
                Notched(px: 2)
                    .stroke(targeted ? accent : Pal.mutedC.opacity(0.5), style: StrokeStyle(lineWidth: 2, dash: [4, 4]))
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onDrop(of: [UTType.fileURL], isTargeted: $targeted) { providers in
            load(providers)
            return true
        }
    }

    private var ffmpegNote: some View {
        HStack(alignment: .top, spacing: 8) {
            PixelIcon(name: "lock", color: accent)
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
        .pixelBox(Pal.raisedC, shadow: false)
    }

    private var recent: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("RECENT")
                .font(Fonts.label(9))
                .foregroundStyle(Pal.mutedC)
            ForEach(history.items.prefix(5)) { item in
                Button {
                    if !item.outputs.isEmpty { NSWorkspace.shared.activateFileViewerSelecting(item.outputs) }
                } label: {
                    HStack(spacing: 8) {
                        Rectangle().fill(accent).frame(width: 6, height: 6)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(item.title).font(.system(size: 12, weight: .semibold)).lineLimit(1)
                            if !item.detail.isEmpty {
                                Text(item.detail).font(.system(size: 10.5)).foregroundStyle(Pal.mutedC).lineLimit(1)
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
                        .foregroundStyle(Pal.mutedC)
                }
                Slider(value: $quality, in: 0.5...1.0).tint(accent).controlSize(.small)
            }

            VStack(alignment: .leading, spacing: 6) {
                Text("Save to").font(label)
                Picker("", selection: $saveTo) {
                    Text("Beside original").tag(Prefs.SaveTo.beside.rawValue)
                    Text("Downloads").tag(Prefs.SaveTo.downloads.rawValue)
                    Text("Folder…").tag(Prefs.SaveTo.custom.rawValue)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                .onChange(of: saveTo) { now in
                    if now == Prefs.SaveTo.custom.rawValue && customFolder.isEmpty {
                        if !chooseFolder() { saveTo = lastSaveTo; return }
                    }
                    lastSaveTo = now
                }
                if saveTo == Prefs.SaveTo.custom.rawValue && !customFolder.isEmpty {
                    HStack(spacing: 8) {
                        Text((customFolder as NSString).abbreviatingWithTildeInPath)
                            .font(Fonts.body(11))
                            .foregroundStyle(Pal.mutedC)
                            .lineLimit(1)
                            .truncationMode(.middle)
                            .help(customFolder)
                        Spacer(minLength: 4)
                        Button("Change") { _ = chooseFolder() }
                            .buttonStyle(.plain)
                            .font(Fonts.label(9))
                            .foregroundStyle(accent)
                    }
                } else if saveTo == Prefs.SaveTo.downloads.rawValue {
                    Text("~/Downloads/Chomped")
                        .font(Fonts.body(11))
                        .foregroundStyle(Pal.mutedC)
                }
            }

            skins

            row("Pixel text", $pixelText)
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

    private var label: Font { Fonts.display(14, .regular) }

    private var skins: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Chest skin").font(label)
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                ForEach(Skin.allCases) { k in
                    let on = skin == k
                    Button {
                        if !k.locked { skinRaw = k.rawValue }
                    } label: {
                        VStack(spacing: 3) {
                            ZStack(alignment: .topTrailing) {
                                MimicPortrait(skin: k, scale: 1)
                                    .opacity(k.locked ? 0.35 : 1)
                                if k.locked { PixelIcon(name: "lock", scale: 1, color: Pal.mutedC) }
                            }
                            Text(k.title).font(Fonts.label(8))
                            Text(k.locked ? "\(Stats.chomps)/\(Skin.royalAt)" : k.note)
                                .font(Fonts.body(9))
                                .foregroundStyle(Pal.mutedC)
                                .lineLimit(1)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 7)
                        .background(Rectangle().fill(on ? k.accent.opacity(0.18) : Pal.raisedC))
                        .overlay(Rectangle().strokeBorder(on ? k.accent : Pal.inkC, lineWidth: 2))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help(k.locked ? "Unlocks after \(Skin.royalAt) chomps" : k.title)
                }
            }
        }
    }

    private func row(_ title: String, _ value: Binding<Bool>) -> some View {
        HStack {
            Text(title).font(label)
            Spacer()
            Toggle("", isOn: value).labelsHidden().toggleStyle(.switch).controlSize(.mini).tint(accent)
        }
    }

    /// Returns false if the person cancels.
    private func chooseFolder() -> Bool {
        NSApp.activate(ignoringOtherApps: true)
        let p = NSOpenPanel()
        p.canChooseFiles = false
        p.canChooseDirectories = true
        p.canCreateDirectories = true
        p.allowsMultipleSelection = false
        p.prompt = "Save Here"
        p.message = "Chomp will save everything it makes into this folder."
        if !customFolder.isEmpty { p.directoryURL = URL(fileURLWithPath: customFolder) }
        guard p.runModal() == .OK, let url = p.url else { return false }
        customFolder = url.path
        FolderAccess.shared.remember(url)
        return true
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
