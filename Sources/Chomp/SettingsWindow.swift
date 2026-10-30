import AppKit
import SwiftUI

/// The settings that don't fit in the menu bar panel: size limit, watched folder, clipboard, updates and which
/// slices the wheel shows.
@MainActor
enum SettingsWindow {
    private static var window: NSWindow?

    static func show() {
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 640),
                             styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
            w.title = "Chomp settings"
            w.titlebarAppearsTransparent = true
            w.isReleasedWhenClosed = false
            w.backgroundColor = NSColor(Pal.panelC)
            w.contentView = NSHostingView(rootView: MoreSettings())
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}

struct MoreSettings: View {
    @AppStorage("skin") private var skinRaw = Skin.current.rawValue
    @AppStorage("sizeLimit") private var sizeLimit = Prefs.sizeLimit
    @AppStorage("watchFolder") private var watchFolder = ""
    @AppStorage("watchRule") private var watchRule = Prefs.watchRule
    @AppStorage("shrinkClipboard") private var shrinkClipboard = false
    @AppStorage("checkUpdates") private var checkUpdates = true
    @State private var hidden = Prefs.hiddenSlices
    @State private var kind: Kind = .image
    var scrolls = true   // the snapshot renderer can't draw scroll views

    private static let names: [Kind: String] = [.image: "Images", .video: "Videos", .audio: "Audio", .pdf: "PDFs", .document: "Docs", .other: "Actions"]

    private var skin: Skin { Skin(rawValue: skinRaw) ?? .oak }
    private var label: Font { Fonts.display(15, .regular) }

    var body: some View {
        Group {
            if scrolls { ScrollView { content } } else { content }
        }
        .frame(width: 520, height: scrolls ? 640 : nil)
        .background(Pal.panelC)
        .foregroundStyle(Pal.toothC)
        .environment(\.colorScheme, .dark)
    }

    private var content: some View {
            VStack(alignment: .leading, spacing: 22) {
                section("Size limit", "What the “fit” slices aim under. Discord allows 10 MB, most email 25 MB.") {
                    HStack(spacing: 8) {
                        ForEach(Prefs.sizeLimits, id: \.self) { mb in
                            key("\(mb) MB", on: sizeLimit == mb) { sizeLimit = mb }
                        }
                    }
                }

                section("Watch a folder", "New files that land in it get chewed automatically. The originals stay.") {
                    HStack(spacing: 8) {
                        key(watchFolder.isEmpty ? "Choose folder…" : "Change…", on: false) { chooseFolder() }
                        if !watchFolder.isEmpty {
                            key("Stop", on: false) {
                                watchFolder = ""
                                FolderWatcher.shared.restart()
                            }
                        }
                    }
                    if !watchFolder.isEmpty {
                        Text((watchFolder as NSString).abbreviatingWithTildeInPath)
                            .font(Fonts.body(12)).foregroundStyle(Pal.mutedC).lineLimit(1).truncationMode(.middle)
                        Picker("", selection: $watchRule) {
                            ForEach(WatchRule.allCases) { Text($0.title).tag($0.rawValue) }
                        }
                        .labelsHidden()
                        .frame(maxWidth: 280)
                    }
                }

                section("Clipboard", "Copy a big screenshot and Chomp swaps it for a much smaller JPG, ready to paste into chats.") {
                    toggle("Shrink copied pictures", $shrinkClipboard)
                }

                section("Updates", "Asks GitHub once a day if there's a newer Chomp. Nothing about you or your files is sent.") {
                    toggle("Check for updates", $checkUpdates)
                        .onChange(of: checkUpdates) { on in if on { Updates.shared.check() } else { Updates.shared.available = nil } }
                }

                section("Wheel slices", "Switch off the slices you never use.") {
                    HStack(spacing: 6) {
                        ForEach([Kind.image, .video, .audio, .pdf, .document, .other], id: \.self) { k in
                            key(Self.names[k] ?? "", on: kind == k) { kind = k }
                        }
                    }
                    ForEach(kind == .other ? [Mode.actions] : [Mode.convert, .tools], id: \.self) { m in
                        let ops = Catalog.allOps(m, kind)
                        if !ops.isEmpty {
                            Text(m.title.uppercased()).font(Fonts.label(9)).foregroundStyle(Pal.mutedC).padding(.top, 4)
                            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                                ForEach(ops) { op in
                                    let k = Catalog.sliceKey(op, m, kind)
                                    key(op.title, on: !hidden.contains(k), width: .infinity) {
                                        if hidden.contains(k) { hidden.remove(k) } else { hidden.insert(k) }
                                        Prefs.hiddenSlices = hidden
                                    }
                                }
                            }
                        }
                    }
                }
            }
            .padding(EdgeInsets(top: 40, leading: 24, bottom: 24, trailing: 24))
    }

    private func section<C: View>(_ title: String, _ note: String, @ViewBuilder _ content: () -> C) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).font(Fonts.display(18)).foregroundStyle(skin.accent)
            Text(note).font(Fonts.body(12)).foregroundStyle(Pal.mutedC).fixedSize(horizontal: false, vertical: true)
            content()
        }
    }

    private func key(_ t: String, on: Bool, width: CGFloat? = nil, _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Keycap(on: on, skin: skin) {
                Text(t).font(Fonts.label(10)).lineLimit(1).padding(.horizontal, 12).frame(maxWidth: width).frame(height: 28)
            }
        }
        .buttonStyle(.plain)
    }

    private func toggle(_ t: String, _ v: Binding<Bool>) -> some View {
        HStack {
            Text(t).font(label)
            Spacer()
            Toggle("", isOn: v).labelsHidden().toggleStyle(.switch).controlSize(.mini).tint(skin.accent)
        }
    }

    private func chooseFolder() {
        let p = NSOpenPanel()
        p.canChooseFiles = false
        p.canChooseDirectories = true
        p.canCreateDirectories = true
        p.prompt = "Watch"
        p.message = "Chomp will chew new files that land in this folder."
        guard p.runModal() == .OK, let url = p.url else { return }
        watchFolder = url.path
        FolderWatcher.shared.restart()
    }
}
