import Foundation
import UniformTypeIdentifiers

enum Kind: String {
    case image, video, audio, pdf, document, folder, other

    var noun: String {
        switch self {
        case .image: return "image"
        case .video: return "video"
        case .audio: return "audio file"
        case .pdf: return "PDF"
        case .document: return "document"
        case .folder: return "folder"
        case .other: return "file"
        }
    }
}

enum Mode: Int, CaseIterable, Identifiable {
    case convert, tools, actions
    var id: Int { rawValue }
    var title: String { ["Convert", "Tools", "Actions"][rawValue] }
    var key: String { ["⇧", "⌘", "⌃"][rawValue] }
    var next: Mode { Mode(rawValue: (rawValue + 1) % 3)! }
}

struct Op: Identifiable, Equatable {
    let id: String
    let title: String
    let detail: String
    let symbol: String?
    let kinds: Set<Kind>
    let batch: Bool
}

enum Catalog {
    // MARK: File types

    static func norm(_ ext: String) -> String {
        switch ext.lowercased() {
        case "jpeg", "jpe": return "jpg"
        case "tif": return "tiff"
        case "heif": return "heic"
        case "aif": return "aiff"
        case "htm": return "html"
        case "markdown": return "md"
        case "m4v": return "mp4"
        default: return ext.lowercased()
        }
    }

    static func kind(of url: URL) -> Kind {
        let ext = norm(url.pathExtension)
        var isDir: ObjCBool = false
        if FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir), isDir.boolValue, ext != "rtfd" {
            return .folder
        }
        switch ext {
        case "jpg", "png", "heic", "webp", "avif", "tiff", "gif", "bmp": return .image
        case "mp4", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "3gp": return .video
        case "mp3", "m4a", "wav", "flac", "aiff", "ogg", "opus", "aac", "wma", "caf": return .audio
        case "pdf": return .pdf
        case "doc", "docx", "rtf", "rtfd", "txt", "md", "html", "odt", "webarchive", "wordml": return .document
        default: break
        }
        guard let t = UTType(filenameExtension: ext) else { return .other }
        if t.conforms(to: .image) { return .image }
        if t.conforms(to: .movie) { return .video }
        if t.conforms(to: .audio) { return .audio }
        if t.conforms(to: .pdf) { return .pdf }
        if t.conforms(to: .text) { return .document }
        return .other
    }

    /// Folders become the files inside them (recursive, hidden files skipped).
    static func expand(_ urls: [URL], limit: Int = 10_000) -> [URL] {
        var out: [URL] = []
        let fm = FileManager.default
        for u in urls {
            if kind(of: u) == .folder {
                guard let e = fm.enumerator(at: u, includingPropertiesForKeys: [.isRegularFileKey],
                                            options: [.skipsHiddenFiles, .skipsPackageDescendants]) else { continue }
                for case let f as URL in e {
                    if (try? f.resourceValues(forKeys: [.isRegularFileKey]))?.isRegularFile == true {
                        out.append(f)
                    }
                    if out.count >= limit { return out }
                }
            } else {
                out.append(u)
            }
        }
        return out
    }

    /// The most common file kind in a drop, and how many files that drop really is.
    static func summary(_ urls: [URL]) -> (kind: Kind, count: Int) {
        let files = expand(urls, limit: 1500)
        var counts: [Kind: Int] = [:]
        for f in files {
            let k = kind(of: f)
            if k != .other { counts[k, default: 0] += 1 }
        }
        let k = counts.max { $0.value < $1.value }?.key ?? .other
        return (k, files.count)
    }

    // MARK: Operations

    private static func o(_ id: String, _ t: String, _ d: String, _ s: String?, _ k: Set<Kind>, batch: Bool = false) -> Op {
        Op(id: id, title: t, detail: d, symbol: s, kinds: k, batch: batch)
    }

    private static let everything: Set<Kind> = [.image, .video, .audio, .pdf, .document, .folder, .other]

    static func ops(_ mode: Mode, _ kind: Kind) -> [Op] {
        switch mode {
        case .convert: return convert(kind)
        case .tools: return tools(kind)
        case .actions: return actions
        }
    }

    private static func convert(_ kind: Kind) -> [Op] {
        switch kind {
        case .image:
            let K: Set<Kind> = [.image]
            return [
                o("to.jpg", "JPG", "Universal", nil, K),
                o("to.png", "PNG", "Lossless", nil, K),
                o("to.webp", "WebP", "For web", nil, K),
                o("to.heic", "HEIC", "Tiny, Apple", nil, K),
                o("to.avif", "AVIF", "Smallest", nil, K),
                o("to.pdf", "PDF", "Document", nil, K),
                o("to.tiff", "TIFF", "Print", nil, K),
                o("to.gif", "GIF", "Old school", nil, K),
            ]
        case .video:
            let K: Set<Kind> = [.video]
            return [
                o("to.mp4", "MP4", "H.264", nil, K),
                o("to.mov", "MOV", "QuickTime", nil, K),
                o("to.gif", "GIF", "Loop", nil, K),
                o("to.webm", "WebM", "VP9 · web", nil, K),
                o("to.mp3", "MP3", "Audio only", nil, K),
                o("to.m4a", "M4A", "AAC audio", nil, K),
                o("to.mkv", "MKV", "Matroska", nil, K),
            ]
        case .audio:
            let K: Set<Kind> = [.audio]
            return [
                o("to.mp3", "MP3", "Plays anywhere", nil, K),
                o("to.m4a", "M4A", "AAC", nil, K),
                o("to.wav", "WAV", "Uncompressed", nil, K),
                o("to.flac", "FLAC", "Lossless", nil, K),
                o("to.aiff", "AIFF", "Pro audio", nil, K),
                o("to.ogg", "OGG", "Vorbis", nil, K),
                o("to.opus", "Opus", "Voice · web", nil, K),
            ]
        case .pdf:
            let K: Set<Kind> = [.pdf]
            return [
                o("to.png", "PNG", "Every page", nil, K),
                o("to.jpg", "JPG", "Every page", nil, K),
                o("to.txt", "TXT", "Plain text", nil, K),
                o("to.docx", "DOCX", "Editable", nil, K),
                o("to.rtf", "RTF", "Rich text", nil, K),
            ]
        case .document:
            let K: Set<Kind> = [.document]
            return [
                o("to.pdf", "PDF", "Print-ready", nil, K),
                o("to.docx", "DOCX", "Word", nil, K),
                o("to.rtf", "RTF", "Rich text", nil, K),
                o("to.txt", "TXT", "Plain", nil, K),
                o("to.html", "HTML", "Web page", nil, K),
                o("to.odt", "ODT", "OpenDoc", nil, K),
                o("to.doc", "DOC", "Word 97", nil, K),
            ]
        default:
            return []
        }
    }

    private static func tools(_ kind: Kind) -> [Op] {
        let squeeze = "arrow.down.right.and.arrow.up.left"
        switch kind {
        case .image:
            let K: Set<Kind> = [.image]
            var l = [
                o("img.compress", "Compress", "Smaller file", squeeze, K),
                o("img.1080", "1080", "Web / Insta", "aspectratio", K),
                o("img.half", "Half", "50% size", "minus.magnifyingglass", K),
            ]
            if #available(macOS 14.0, *) {
                l.append(o("img.cutout", "Cut out", "Remove BG", "person.crop.square", K))
            }
            l += [
                o("img.strip", "Clean", "No EXIF / GPS", "eye.slash", K),
                o("img.rotate", "Rotate", "90° right", "rotate.right", K),
                o("img.pdf", "Combine", "One PDF", "doc.on.doc", K, batch: true),
            ]
            return l
        case .video:
            let K: Set<Kind> = [.video]
            return [
                o("vid.compress", "Compress", "Smaller MP4", squeeze, K),
                o("vid.10mb", "10 MB", "Send-able", "paperplane", K),
                o("vid.half", "Half", "50% size", "minus.magnifyingglass", K),
                o("vid.frame", "Frame", "Best still", "photo", K),
                o("vid.mute", "Mute", "No audio", "speaker.slash", K),
                o("vid.x2", "2×", "Speed up", "hare", K),
            ]
        case .audio:
            let K: Set<Kind> = [.audio]
            return [
                o("aud.compress", "Compress", "128k MP3", squeeze, K),
                o("aud.loud", "Loudness", "−14 LUFS", "waveform", K),
                o("aud.trim", "Trim", "Silent ends", "scissors", K),
                o("aud.mono", "Mono", "One channel", "circle.lefthalf.filled", K),
            ]
        case .pdf:
            let K: Set<Kind> = [.pdf]
            return [
                o("pdf.compress", "Compress", "Screen quality", squeeze, K),
                o("pdf.merge", "Merge", "One PDF", "doc.on.doc", K, batch: true),
                o("pdf.split", "Split", "Page files", "square.split.2x1", K),
                o("pdf.rotate", "Rotate", "90° right", "rotate.right", K),
            ]
        case .document:
            return [
                o("doc.count", "Count", "Words", "number", [.document, .pdf]),
                o("doc.merge", "Combine", "One PDF", "doc.on.doc", [.document, .pdf], batch: true),
                o("doc.clean", "Clean", "Plain text", "textformat", [.document]),
            ]
        default:
            return []
        }
    }

    private static var actions: [Op] {
        let K = everything
        return [
            o("act.airdrop", "AirDrop", "Send nearby", "wave.3.right", K),
            o("act.copy", "Copy", "To clipboard", "doc.on.clipboard", K),
            o("act.path", "Path", "Copy path", "link", K),
            o("act.zip", "Zip", "Archive", "archivebox", K),
            o("act.terminal", "Terminal", "Open here", "terminal", K),
            o("act.desktop", "Desktop", "Copy there", "menubar.dock.rectangle", K),
        ]
    }
}
