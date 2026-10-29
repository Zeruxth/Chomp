import Foundation

struct RunResult {
    var outputs: [URL] = []
    var bytesIn: Int64 = 0
    var bytesOut: Int64 = 0
    var failures: [String] = []
    var skipped = 0
    var note: String?
}

/// Runs one wheel choice over a drop (files and/or folders).
enum Engine {
    static func run(_ op: Op, items: [URL]) async -> RunResult {
        if op.id.hasPrefix("act.") { return await Actions.run(op, items) }

        let files = Catalog.expand(items).filter { op.kinds.contains(Catalog.kind(of: $0)) }
        var r = RunResult()
        guard !files.isEmpty else {
            r.failures.append("No matching files")
            return r
        }

        if op.id == "doc.count" { return Docs.count(files) }

        if op.batch {
            do {
                let out = try await batch(op, files)
                r.outputs = [out]
                r.bytesIn = files.reduce(0) { $0 + Files.size($1) }
                r.bytesOut = Files.size(out)
                r.note = "\(files.count) → 1 \(out.pathExtension.uppercased())"
            } catch {
                r.failures.append(error.localizedDescription)
            }
            return r
        }

        var queue = files[...]
        if op.id.hasPrefix("to.") {
            let target = String(op.id.dropFirst(3))
            let before = queue.count
            queue = queue.filter { Catalog.norm($0.pathExtension) != target }[...]
            r.skipped = before - queue.count
        }

        let work: (URL) async -> (URL, Result<[URL], Error>) = { f in
            do { return (f, .success(try await perform(op, f))) } catch { return (f, .failure(error)) }
        }

        await withTaskGroup(of: (URL, Result<[URL], Error>).self) { group in
            let limit = max(2, min(4, ProcessInfo.processInfo.activeProcessorCount / 2))
            for _ in 0..<min(limit, queue.count) {
                let f = queue.removeFirst()
                group.addTask { await work(f) }
            }
            for await (f, res) in group {
                switch res {
                case .success(let outs):
                    r.outputs += outs
                    r.bytesIn += Files.size(f)
                    r.bytesOut += outs.reduce(0) { $0 + Files.size($1) }
                case .failure(let e):
                    r.failures.append("\(f.lastPathComponent): \(e.localizedDescription)")
                }
                if !queue.isEmpty {
                    let next = queue.removeFirst()
                    group.addTask { await work(next) }
                }
            }
        }
        return r
    }

    private static func batch(_ op: Op, _ files: [URL]) async throws -> URL {
        let sorted = files.sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
        let out = try Files.dest(named: "Combined", near: sorted[0], ext: "pdf")
        switch op.id {
        case "img.pdf": try Images.toPDF(sorted, out: out)
        case "pdf.merge": try PDFs.merge(sorted, out: out)
        case "doc.merge": try await Docs.mergeToPDF(sorted, out: out)
        default: throw ChompError.msg("Unknown operation")
        }
        return out
    }

    private static func perform(_ op: Op, _ src: URL) async throws -> [URL] {
        let kind = Catalog.kind(of: src)
        let ext = Catalog.norm(src.pathExtension)
        let q = Prefs.quality

        if op.id.hasPrefix("to.") {
            let t = String(op.id.dropFirst(3))
            switch kind {
            case .image:
                return [try await Images.convert(src, to: t, out: try Files.dest(src, ext: t), quality: q)]
            case .video, .audio:
                return [try await Media.convert(src, to: t)]
            case .pdf:
                return try PDFs.convert(src, to: t)
            case .document:
                return [try await Docs.convert(src, to: t, out: try Files.dest(src, ext: t))]
            default:
                throw ChompError.msg("Unsupported file")
            }
        }

        switch op.id {
        // Images
        case "img.compress":
            let t = Images.compressTarget(src)
            return [try await Images.convert(src, to: t, out: try Files.dest(src, ext: t, suffix: "small"), quality: min(q, 0.7))]
        case "img.1080":
            let t = Images.keepTarget(ext)
            return [try await Images.convert(src, to: t, out: try Files.dest(src, ext: t, suffix: "1080"), quality: q, maxPixel: 1080)]
        case "img.half":
            let t = Images.keepTarget(ext)
            let d = Images.dims(src)
            return [try await Images.convert(src, to: t, out: try Files.dest(src, ext: t, suffix: "half"),
                                             quality: q, maxPixel: max(1, max(d.0, d.1) / 2))]
        case "img.strip":
            let t = Images.keepTarget(ext)
            return [try await Images.convert(src, to: t, out: try Files.dest(src, ext: t, suffix: "clean"),
                                             quality: max(q, 0.9), strip: true)]
        case "img.rotate":
            let t = Images.keepTarget(ext)
            return [try await Images.rotate(src, ext: t, out: try Files.dest(src, ext: t, suffix: "rotated"), quality: max(q, 0.9))]
        case "img.cutout":
            if #available(macOS 14.0, *) {
                return [try Images.cutout(src, out: try Files.dest(src, ext: "png", suffix: "cutout"))]
            }
            throw ChompError.msg("Needs macOS 14")

        // Video
        case "vid.compress":
            return [try await Media.ff(src, ext: "mp4", suffix: "small", Media.h264(crf: 28, audio: "128k") + ["-movflags", "+faststart"])]
        case "vid.10mb":
            return [try await Media.under(src, mb: 10)]
        case "vid.half":
            return [try await Media.ff(src, ext: "mp4", suffix: "half", Media.h264(crf: 23, vf: "scale=trunc(iw/4)*2:trunc(ih/4)*2"))]
        case "vid.frame":
            return [try await Media.ff(src, ext: "png", suffix: "frame", ["-vf", "thumbnail=120", "-frames:v", "1"])]
        case "vid.mute":
            return [try await Media.ff(src, ext: ext, suffix: "muted", ["-an", "-c:v", "copy"])]
        case "vid.x2":
            return [try await Media.ff(src, ext: "mp4", suffix: "2x",
                                       Media.h264(crf: 22, vf: "setpts=0.5*PTS," + Media.even) + ["-af", "atempo=2.0"])]

        // Audio
        case "aud.compress":
            return [try await Media.ff(src, ext: "mp3", suffix: "small", ["-vn", "-c:a", "libmp3lame", "-b:a", "128k"])]
        case "aud.loud":
            let t = Media.audioTarget(ext)
            return [try await Media.ff(src, ext: t, suffix: "loud", ["-vn", "-af", "loudnorm=I=-14:TP=-1:LRA=11"] + Media.audioCodec(t))]
        case "aud.trim":
            let t = Media.audioTarget(ext)
            let f = "silenceremove=start_periods=1:start_threshold=-50dB,areverse,silenceremove=start_periods=1:start_threshold=-50dB,areverse"
            return [try await Media.ff(src, ext: t, suffix: "trimmed", ["-vn", "-af", f] + Media.audioCodec(t))]
        case "aud.mono":
            let t = Media.audioTarget(ext)
            return [try await Media.ff(src, ext: t, suffix: "mono", ["-vn", "-ac", "1"] + Media.audioCodec(t))]

        // PDF
        case "pdf.compress": return [try PDFs.compress(src)]
        case "pdf.split": return try PDFs.split(src)
        case "pdf.rotate": return [try PDFs.rotate(src)]

        // Documents
        case "doc.clean":
            return [try await Docs.convert(src, to: "txt", out: try Files.dest(src, ext: "txt", suffix: "clean"))]

        default:
            throw ChompError.msg("Unknown operation")
        }
    }
}
