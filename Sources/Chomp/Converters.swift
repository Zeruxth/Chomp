import AppKit
import AVFoundation
import CoreImage
import ImageIO
import PDFKit
import Vision

// MARK: - Images (ImageIO, built-in libwebp for WebP, ffmpeg for AVIF only on Macs that can't write it)

enum Images {
    static let utis: [String: String] = [
        "jpg": "public.jpeg", "png": "public.png", "heic": "public.heic", "tiff": "public.tiff",
        "gif": "com.compuserve.gif", "bmp": "com.microsoft.bmp",
        "webp": "org.webmproject.webp", "avif": "public.avif",
    ]

    static func canWrite(_ ext: String) -> Bool {
        guard let u = utis[ext] else { return false }
        let ids = CGImageDestinationCopyTypeIdentifiers() as? [String] ?? []
        return ids.contains(u)
    }

    static func canSave(_ ext: String) -> Bool {
        canWrite(ext) || ext == "webp" || (ext == "avif" && Bin.av1Encoder != nil)
    }

    /// Same format if we can write it, otherwise PNG.
    static func keepTarget(_ ext: String) -> String { canSave(ext) ? ext : "png" }

    /// Format that actually gets smaller.
    static func compressTarget(_ src: URL) -> String {
        let ext = Catalog.norm(src.pathExtension)
        switch ext {
        case "jpg", "heic": return ext
        case "webp", "avif": return canSave(ext) ? ext : "jpg"
        case "png": return hasAlpha(src) ? "webp" : "jpg"
        default: return "jpg"
        }
    }

    static func props(_ src: URL) -> [CFString: Any] {
        guard let s = CGImageSourceCreateWithURL(src as CFURL, nil) else { return [:] }
        return CGImageSourceCopyPropertiesAtIndex(s, 0, nil) as? [CFString: Any] ?? [:]
    }

    static func dims(_ src: URL) -> (Int, Int) {
        let p = props(src)
        var w = p[kCGImagePropertyPixelWidth] as? Int ?? 0
        var h = p[kCGImagePropertyPixelHeight] as? Int ?? 0
        if let o = p[kCGImagePropertyOrientation] as? Int, o >= 5 { swap(&w, &h) }
        return (w, h)
    }

    static func hasAlpha(_ src: URL) -> Bool { props(src)[kCGImagePropertyHasAlpha] as? Bool ?? false }

    /// Decodes upright (EXIF orientation applied), optionally capped to a long edge.
    static func load(_ src: URL, maxPixel: Int? = nil) throws -> CGImage {
        guard let s = CGImageSourceCreateWithURL(src as CFURL, nil) else { throw ChompError.msg("Can't read image") }
        let d = dims(src)
        let full = max(d.0, d.1)
        let target = maxPixel.map { min($0, full) } ?? full
        if target > 0 {
            let o: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: target,
                kCGImageSourceShouldCacheImmediately: true,
            ]
            if let img = CGImageSourceCreateThumbnailAtIndex(s, 0, o as CFDictionary) { return img }
        }
        guard let img = CGImageSourceCreateImageAtIndex(s, 0, nil) else { throw ChompError.msg("Can't decode image") }
        return img
    }

    /// Source metadata minus anything that would re-rotate or mis-size the new file.
    static func metadata(_ src: URL) -> [CFString: Any] {
        var p = props(src)
        for k in [kCGImagePropertyPixelWidth, kCGImagePropertyPixelHeight, kCGImagePropertyOrientation] {
            p.removeValue(forKey: k)
        }
        if var tiff = p[kCGImagePropertyTIFFDictionary] as? [CFString: Any] {
            tiff.removeValue(forKey: kCGImagePropertyTIFFOrientation)
            p[kCGImagePropertyTIFFDictionary] = tiff
        }
        return p
    }

    static func alpha(_ i: CGImage) -> Bool {
        switch i.alphaInfo {
        case .none, .noneSkipFirst, .noneSkipLast: return false
        default: return true
        }
    }

    static func context(_ w: Int, _ h: Int) -> CGContext? {
        CGContext(data: nil, width: w, height: h, bitsPerComponent: 8, bytesPerRow: 0,
                  space: CGColorSpace(name: CGColorSpace.sRGB)!,
                  bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    }

    static func flatten(_ i: CGImage) -> CGImage {
        guard let c = context(i.width, i.height) else { return i }
        let r = CGRect(x: 0, y: 0, width: i.width, height: i.height)
        c.setFillColor(CGColor(gray: 1, alpha: 1))
        c.fill(r)
        c.draw(i, in: r)
        return c.makeImage() ?? i
    }

    static func write(_ img: CGImage, to out: URL, ext: String, quality: Double, meta: [CFString: Any]) throws {
        guard let uti = utis[ext] else { throw ChompError.msg("Unknown format") }
        var image = img
        if ext == "jpg" || ext == "bmp", alpha(img) { image = flatten(img) }
        guard let d = CGImageDestinationCreateWithURL(out as CFURL, uti as CFString, 1, nil) else {
            throw ChompError.msg("Can't write \(ext.uppercased())")
        }
        var p = meta
        // macOS can't write lossless AVIF: at quality 1.0 it fails outright, so top out just below.
        p[kCGImageDestinationLossyCompressionQuality] = ext == "avif" ? min(quality, 0.99) : quality
        CGImageDestinationAddImage(d, image, p as CFDictionary)
        guard CGImageDestinationFinalize(d) else { throw ChompError.msg("Couldn't save \(ext.uppercased())") }
    }

    /// Writes with ImageIO, or via a temp PNG + ffmpeg for WebP/AVIF.
    static func save(_ img: CGImage, to out: URL, ext: String, quality: Double, meta: [CFString: Any]) async throws {
        if ext == "webp" { try WebP.write(img, to: out, quality: quality); return }
        if canWrite(ext) { try write(img, to: out, ext: ext, quality: quality, meta: meta); return }
        guard ext == "avif" else { throw ChompError.msg("Can't write \(ext.uppercased())") }
        // Only Macs whose ImageIO can't write AVIF get here.
        guard let ff = Bin.ffmpeg else { throw ChompError.needsFFmpeg }
        guard let enc = Bin.av1Encoder else { throw ChompError.msg("This Mac can't make AVIF yet") }
        let tmp = Files.tempURL("png")
        try write(img, to: tmp, ext: "png", quality: 1, meta: [:])
        defer { try? FileManager.default.removeItem(at: tmp) }
        let crf = String(Int((1 - quality) * 50) + 12)
        var args = ["-y", "-nostdin", "-loglevel", "error", "-i", tmp.path, "-c:v", enc, "-crf", crf, "-pix_fmt", "yuv420p"]
        args += enc == "libaom-av1" ? ["-still-picture", "1", "-cpu-used", "6"] : ["-preset", "8"]
        do { try await Shell.run(ff, args + [out.path]) } catch {
            try? FileManager.default.removeItem(at: out)
            throw error
        }
    }

    static func convert(_ src: URL, to ext: String, out: URL, quality: Double,
                        maxPixel: Int? = nil, strip: Bool = false) async throws -> URL {
        if ext == "pdf" { try toPDF([src], out: out); return out }
        let img = try load(src, maxPixel: maxPixel)
        try await save(img, to: out, ext: ext, quality: quality, meta: strip ? [:] : metadata(src))
        return out
    }

    static func rotate(_ src: URL, ext: String, out: URL, quality: Double) async throws -> URL {
        let i = try load(src)
        guard let c = context(i.height, i.width) else { throw ChompError.msg("Rotate failed") }
        c.translateBy(x: 0, y: CGFloat(i.width))
        c.rotate(by: -.pi / 2)
        c.draw(i, in: CGRect(x: 0, y: 0, width: i.width, height: i.height))
        guard let r = c.makeImage() else { throw ChompError.msg("Rotate failed") }
        try await save(r, to: out, ext: ext, quality: quality, meta: metadata(src))
        return out
    }

    @available(macOS 14.0, *)
    static func cutout(_ src: URL, out: URL) throws -> URL {
        let i = try load(src)
        let req = VNGenerateForegroundInstanceMaskRequest()
        let handler = VNImageRequestHandler(cgImage: i, options: [:])
        try handler.perform([req])
        guard let obs = req.results?.first, !obs.allInstances.isEmpty else {
            throw ChompError.msg("No subject found")
        }
        let buf = try obs.generateMaskedImage(ofInstances: obs.allInstances, from: handler, croppedToInstancesExtent: false)
        let ci = CIImage(cvPixelBuffer: buf)
        guard let cg = CIContext().createCGImage(ci, from: ci.extent) else { throw ChompError.msg("Cut-out failed") }
        try write(cg, to: out, ext: "png", quality: 1, meta: [:])
        return out
    }

    static func toPDF(_ urls: [URL], out: URL) throws {
        let doc = PDFDocument()
        for u in urls {
            guard let cg = try? load(u) else { continue }
            let ns = NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
            if let page = PDFPage(image: ns) { doc.insert(page, at: doc.pageCount) }
        }
        guard doc.pageCount > 0, doc.write(to: out) else { throw ChompError.msg("PDF failed") }
    }
}

// MARK: - Video & audio (ffmpeg; AVFoundation fallback for MP4/MOV/M4A)

enum Media {
    static let even = "scale=trunc(iw/2)*2:trunc(ih/2)*2"

    static func h264(crf: Int, vf: String = even, audio: String = "192k") -> [String] {
        ["-vf", vf, "-c:v", "libx264", "-preset", "veryfast", "-crf", String(crf),
         "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audio]
    }

    static func audioTarget(_ ext: String) -> String {
        ["mp3", "m4a", "wav", "flac", "aiff", "ogg", "opus"].contains(ext) ? ext : "m4a"
    }

    static func audioCodec(_ ext: String) -> [String] {
        switch ext {
        case "mp3": return ["-c:a", "libmp3lame", "-q:a", "2"]
        case "m4a": return ["-c:a", "aac", "-b:a", "256k"]
        case "wav": return ["-c:a", "pcm_s16le"]
        case "flac": return ["-c:a", "flac"]
        case "aiff": return ["-c:a", "pcm_s16be"]
        case "ogg": return ["-c:a", "libvorbis", "-q:a", "6"]
        case "opus": return ["-c:a", "libopus", "-b:a", "128k"]
        default: return []
        }
    }

    static func ff(_ src: URL, ext: String, suffix: String?, _ args: [String]) async throws -> URL {
        guard let ff = Bin.ffmpeg else { throw ChompError.needsFFmpeg }
        let out = try Files.dest(src, ext: ext, suffix: suffix)
        do {
            try await Shell.run(ff, ["-y", "-nostdin", "-loglevel", "error", "-i", src.path] + args + [out.path])
        } catch {
            try? FileManager.default.removeItem(at: out)
            throw error
        }
        return out
    }

    static func convert(_ src: URL, to t: String) async throws -> URL {
        guard Bin.ffmpeg != nil else {
            if ["mp4", "mov", "m4a"].contains(t) { return try await avExport(src, to: t) }
            throw ChompError.needsFFmpeg
        }
        let a: [String]
        switch t {
        case "mp4": a = h264(crf: 20) + ["-movflags", "+faststart"]
        case "mov", "mkv": a = h264(crf: 20)
        case "webm":
            a = ["-vf", even, "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1",
                 "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k"]
        case "gif":
            a = ["-vf", "fps=15,scale='min(720,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a",
                 "-loop", "0"]
        default:
            a = ["-vn"] + audioCodec(t)
        }
        return try await ff(src, ext: t, suffix: nil, a)
    }

    /// Fits a video under a size budget with a computed bitrate.
    static func under(_ src: URL, mb: Double) async throws -> URL {
        let dur = try await AVURLAsset(url: src).load(.duration).seconds
        guard dur.isFinite, dur > 0 else { throw ChompError.msg("Can't read duration") }
        let totalK = mb * 8000 * 0.92 / dur
        let videoK = Int(totalK - 96)
        guard videoK >= 80 else { throw ChompError.msg("Too long to fit \(Int(mb)) MB") }
        let vf = videoK < 1500 ? "scale=w='trunc(min(1280,iw)/2)*2':h=-2" : even
        return try await ff(src, ext: "mp4", suffix: "\(Int(mb))MB", [
            "-vf", vf, "-c:v", "libx264", "-preset", "veryfast",
            "-b:v", "\(videoK)k", "-maxrate", "\(videoK)k", "-bufsize", "\(videoK * 2)k",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart",
        ])
    }

    static func avExport(_ src: URL, to t: String) async throws -> URL {
        let out = try Files.dest(src, ext: t)
        let asset = AVURLAsset(url: src)
        let preset = t == "m4a" ? AVAssetExportPresetAppleM4A : AVAssetExportPresetHighestQuality
        guard let s = AVAssetExportSession(asset: asset, presetName: preset) else {
            throw ChompError.msg("Can't export this file")
        }
        s.outputURL = out
        s.outputFileType = t == "m4a" ? .m4a : (t == "mov" ? .mov : .mp4)
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in
            s.exportAsynchronously { c.resume() }
        }
        guard s.status == .completed else {
            throw ChompError.msg(s.error?.localizedDescription ?? "Export failed")
        }
        return out
    }
}

// MARK: - PDF (PDFKit)

enum PDFs {
    static func open(_ u: URL) throws -> PDFDocument {
        guard let d = PDFDocument(url: u) else { throw ChompError.msg("Can't open PDF") }
        if d.isLocked { throw ChompError.msg("PDF is password-locked") }
        return d
    }

    static func convert(_ src: URL, to t: String) throws -> [URL] {
        let doc = try open(src)
        switch t {
        case "png", "jpg":
            var outs: [URL] = []
            for i in 0..<doc.pageCount {
                guard let page = doc.page(at: i), let img = render(page) else { continue }
                let suffix = doc.pageCount > 1 ? String(format: "p%02d", i + 1) : nil
                let out = try Files.dest(src, ext: t, suffix: suffix)
                try Images.write(img, to: out, ext: t, quality: 0.92, meta: [:])
                outs.append(out)
            }
            return outs
        case "txt":
            let out = try Files.dest(src, ext: "txt")
            try (doc.string ?? "").write(to: out, atomically: true, encoding: .utf8)
            return [out]
        case "docx", "rtf":
            let all = NSMutableAttributedString()
            for i in 0..<doc.pageCount {
                if let a = doc.page(at: i)?.attributedString {
                    all.append(a)
                    all.append(NSAttributedString(string: "\n\n"))
                }
            }
            let type: NSAttributedString.DocumentType = t == "docx" ? .officeOpenXML : .rtf
            let data = try all.data(from: NSRange(location: 0, length: all.length),
                                    documentAttributes: [.documentType: type])
            let out = try Files.dest(src, ext: t)
            try data.write(to: out)
            return [out]
        default:
            throw ChompError.msg("Can't make \(t.uppercased()) from a PDF")
        }
    }

    static func render(_ page: PDFPage, dpi: CGFloat = 220) -> CGImage? {
        let b = page.bounds(for: .mediaBox)
        let turned = abs(page.rotation) % 180 == 90
        let s = dpi / 72
        let size = NSSize(width: (turned ? b.height : b.width) * s, height: (turned ? b.width : b.height) * s)
        let ns = page.thumbnail(of: size, for: .mediaBox)
        var r = NSRect(origin: .zero, size: ns.size)
        guard let cg = ns.cgImage(forProposedRect: &r, context: nil, hints: nil) else { return nil }
        return Images.flatten(cg)
    }

    static func compress(_ src: URL) throws -> URL {
        let doc = try open(src)
        let out = try Files.dest(src, ext: "pdf", suffix: "small")
        let ok: Bool
        if #available(macOS 14.0, *) {
            ok = doc.write(to: out, withOptions: [.saveImagesAsJPEGOption: true, .optimizeImagesForScreenOption: true])
        } else {
            ok = doc.write(to: out)
        }
        guard ok else { throw ChompError.msg("Compress failed") }
        return out
    }

    static func merge(_ urls: [URL], out: URL) throws {
        let doc = PDFDocument()
        for u in urls {
            let d = try open(u)
            for i in 0..<d.pageCount {
                if let p = d.page(at: i)?.copy() as? PDFPage { doc.insert(p, at: doc.pageCount) }
            }
        }
        guard doc.pageCount > 0, doc.write(to: out) else { throw ChompError.msg("Merge failed") }
    }

    static func split(_ src: URL) throws -> [URL] {
        let doc = try open(src)
        var outs: [URL] = []
        for i in 0..<doc.pageCount {
            guard let p = doc.page(at: i)?.copy() as? PDFPage else { continue }
            let one = PDFDocument()
            one.insert(p, at: 0)
            let out = try Files.dest(src, ext: "pdf", suffix: String(format: "p%02d", i + 1))
            if one.write(to: out) { outs.append(out) }
        }
        return outs
    }

    static func rotate(_ src: URL) throws -> URL {
        let doc = try open(src)
        for i in 0..<doc.pageCount { if let p = doc.page(at: i) { p.rotation = (p.rotation + 90) % 360 } }
        let out = try Files.dest(src, ext: "pdf", suffix: "rotated")
        guard doc.write(to: out) else { throw ChompError.msg("Rotate failed") }
        return out
    }
}

// MARK: - Documents (textutil, built into macOS)

enum Docs {
    static func convert(_ src: URL, to t: String, out: URL) async throws -> URL {
        if t == "pdf" {
            try await MainActor.run { try toPDF(src, out: out) }
            return out
        }
        var args = ["-convert", t]
        if Catalog.norm(src.pathExtension) == "md" { args += ["-format", "txt"] }
        try await Shell.run("/usr/bin/textutil", args + [src.path, "-output", out.path])
        return out
    }

    static func attributed(_ src: URL) throws -> NSAttributedString {
        let ext = Catalog.norm(src.pathExtension)
        if ext == "md", let s = try? String(contentsOf: src, encoding: .utf8),
           let a = try? AttributedString(markdown: s, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace)) {
            return NSAttributedString(a)
        }
        if ext == "txt" || ext == "md" {
            let s = (try? String(contentsOf: src, encoding: .utf8)) ?? ""
            return NSAttributedString(string: s, attributes: [.font: NSFont.systemFont(ofSize: 11)])
        }
        return try NSAttributedString(url: src, options: [:], documentAttributes: nil)
    }

    /// Lays the text out on A4 and prints it to a PDF file. Main thread only.
    static func toPDF(_ src: URL, out: URL) throws {
        let attr = try attributed(src)
        let paper = NSSize(width: 595, height: 842)
        let margin: CGFloat = 56
        let info = NSPrintInfo(dictionary: [.jobSavingURL: out])
        info.jobDisposition = .save
        info.paperSize = paper
        info.topMargin = margin; info.bottomMargin = margin
        info.leftMargin = margin; info.rightMargin = margin
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.isHorizontallyCentered = false
        info.isVerticallyCentered = false

        let width = paper.width - margin * 2
        let tv = NSTextView(frame: NSRect(x: 0, y: 0, width: width, height: 100))
        tv.isVerticallyResizable = true
        tv.isHorizontallyResizable = false
        tv.textContainer?.widthTracksTextView = true
        tv.textStorage?.setAttributedString(attr)
        if let lm = tv.layoutManager, let tc = tv.textContainer {
            lm.ensureLayout(for: tc)
            tv.setFrameSize(NSSize(width: width, height: max(lm.usedRect(for: tc).height + 20, 100)))
        }
        let op = NSPrintOperation(view: tv, printInfo: info)
        op.showsPrintPanel = false
        op.showsProgressPanel = false
        guard op.run() else { throw ChompError.msg("PDF export failed") }
    }

    static func mergeToPDF(_ files: [URL], out: URL) async throws {
        var parts: [URL] = []
        var temps: [URL] = []
        defer { temps.forEach { try? FileManager.default.removeItem(at: $0) } }
        for f in files {
            if Catalog.kind(of: f) == .pdf { parts.append(f); continue }
            let t = Files.tempURL("pdf")
            try await MainActor.run { try toPDF(f, out: t) }
            temps.append(t)
            parts.append(t)
        }
        try PDFs.merge(parts, out: out)
    }

    static func count(_ files: [URL]) -> RunResult {
        var words = 0, chars = 0
        for f in files {
            let s: String
            if Catalog.kind(of: f) == .pdf {
                s = PDFDocument(url: f)?.string ?? ""
            } else {
                s = (try? attributed(f).string) ?? ""
            }
            words += s.split { $0.isWhitespace || $0.isNewline }.count
            chars += s.count
        }
        let n = NumberFormatter()
        n.numberStyle = .decimal
        var r = RunResult()
        r.note = "\(n.string(from: NSNumber(value: words)) ?? "\(words)") words · \(n.string(from: NSNumber(value: chars)) ?? "\(chars)") characters"
        return r
    }
}

// MARK: - Actions

enum Actions {
    static func run(_ op: Op, _ urls: [URL]) async -> RunResult {
        var r = RunResult()
        guard let first = urls.first else { return r }
        let n = urls.count
        let things = n == 1 ? first.lastPathComponent : "\(n) items"
        switch op.id {
        case "act.airdrop":
            await MainActor.run {
                NSApp.activate(ignoringOtherApps: true)
                NSSharingService(named: .sendViaAirDrop)?.perform(withItems: urls)
            }
            r.note = "AirDrop · \(things)"
        case "act.copy":
            await MainActor.run {
                let pb = NSPasteboard.general
                pb.clearContents()
                pb.writeObjects(urls.map { $0 as NSURL })
            }
            r.note = "Copied \(things)"
        case "act.path":
            await MainActor.run {
                let pb = NSPasteboard.general
                pb.clearContents()
                pb.setString(urls.map(\.path).joined(separator: "\n"), forType: .string)
            }
            r.note = n == 1 ? first.path : "Copied \(n) paths"
        case "act.zip":
            do {
                let out = try await zip(urls)
                r.outputs = [out]
                r.note = out.lastPathComponent
            } catch {
                r.failures.append(error.localizedDescription)
            }
        case "act.terminal":
            let dir = Catalog.kind(of: first) == .folder ? first : first.deletingLastPathComponent()
            await MainActor.run {
                NSWorkspace.shared.open([dir],
                                        withApplicationAt: URL(fileURLWithPath: "/System/Applications/Utilities/Terminal.app"),
                                        configuration: NSWorkspace.OpenConfiguration())
            }
            r.note = "Terminal · \(dir.lastPathComponent)"
        case "act.desktop":
            let desk = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask)[0]
            for u in urls {
                let d = Files.unique(desk.appendingPathComponent(u.lastPathComponent))
                do {
                    try FileManager.default.copyItem(at: u, to: d)
                    r.outputs.append(d)
                } catch {
                    r.failures.append("\(u.lastPathComponent): \(error.localizedDescription)")
                }
            }
            r.note = "Copied \(things) to Desktop"
        default:
            break
        }
        return r
    }

    static func zip(_ urls: [URL]) async throws -> URL {
        let parent = urls[0].deletingLastPathComponent()
        let base = urls.count == 1 ? urls[0].deletingPathExtension().lastPathComponent : "Archive"
        let out = Files.unique(parent.appendingPathComponent(base).appendingPathExtension("zip"))
        if urls.count == 1 {
            try await Shell.run("/usr/bin/ditto", ["-c", "-k", "--sequesterRsrc", "--keepParent", urls[0].path, out.path])
        } else {
            let names = urls.filter { $0.deletingLastPathComponent().path == parent.path }.map(\.lastPathComponent)
            try await Shell.run("/usr/bin/zip", ["-r", "-q", "-X", out.path] + names, cwd: parent)
        }
        return out
    }
}
