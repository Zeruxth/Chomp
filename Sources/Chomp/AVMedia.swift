import AVFoundation
import CoreMedia

/// Video and audio jobs on Apple's own frameworks. The App Store build has no ffmpeg, so these
/// cover the wheel's video tools and audio compress.
enum AVMedia {
    /// A "smaller" result that came out bigger is thrown away; the original was already small.
    static func keepIfSmaller(_ out: URL, than src: URL) throws -> URL {
        guard Files.size(out) < Files.size(src) else {
            try? FileManager.default.removeItem(at: out)
            throw ChompError.msg("Already small. Kept the original")
        }
        return out
    }

    static func export(_ asset: AVAsset, preset: String, to out: URL, type: AVFileType) async throws {
        guard let s = AVAssetExportSession(asset: asset, presetName: preset) else {
            throw ChompError.msg("Can't export this file")
        }
        s.outputURL = out
        s.outputFileType = type
        s.shouldOptimizeForNetworkUse = true
        await withCheckedContinuation { (c: CheckedContinuation<Void, Never>) in s.exportAsynchronously { c.resume() } }
        guard s.status == .completed else {
            try? FileManager.default.removeItem(at: out)
            throw ChompError.msg(s.error?.localizedDescription ?? "Export failed")
        }
    }

    private static func height(_ asset: AVAsset) async -> CGFloat {
        guard let t = try? await asset.loadTracks(withMediaType: .video).first,
              let size = try? await t.load(.naturalSize),
              let tf = try? await t.load(.preferredTransform) else { return 0 }
        let r = CGRect(origin: .zero, size: size).applying(tf)
        return min(abs(r.width), abs(r.height))
    }

    /// HEVC at up to 1080p: much smaller, plays everywhere Apple.
    static func compress(_ src: URL) async throws -> URL {
        let out = try Files.dest(src, ext: "mp4", suffix: "small")
        try await export(AVURLAsset(url: src), preset: AVAssetExportPresetHEVC1920x1080, to: out, type: .mp4)
        return try keepIfSmaller(out, than: src)
    }

    /// Steps down through sizes until the H.264 file fits under the budget.
    static func under(_ src: URL, mb: Double) async throws -> URL {
        let asset = AVURLAsset(url: src)
        let limit = Int64(mb * 1_000_000 * 0.97)
        let presets = [AVAssetExportPreset1280x720, AVAssetExportPreset960x540, AVAssetExportPreset640x480, AVAssetExportPresetLowQuality]
        for preset in presets {
            if let s = AVAssetExportSession(asset: asset, presetName: preset) {
                s.outputFileType = .mp4
                if let guess = try? await s.estimatedOutputFileLengthInBytes, guess > limit * 2 { continue }
            }
            let out = try Files.dest(src, ext: "mp4", suffix: "\(Int(mb))MB")
            try await export(asset, preset: preset, to: out, type: .mp4)
            if Files.size(out) <= limit { return out }
            try? FileManager.default.removeItem(at: out)
        }
        throw ChompError.msg("Too long to fit \(Int(mb)) MB")
    }

    static func half(_ src: URL) async throws -> URL {
        let asset = AVURLAsset(url: src)
        let h = await height(asset) / 2
        let preset = h >= 1080 ? AVAssetExportPreset1920x1080 : h >= 720 ? AVAssetExportPreset1280x720
            : h >= 540 ? AVAssetExportPreset960x540 : AVAssetExportPreset640x480
        let out = try Files.dest(src, ext: "mp4", suffix: "half")
        try await export(asset, preset: preset, to: out, type: .mp4)
        return try keepIfSmaller(out, than: src)
    }

    /// A still from a third of the way in, saved as PNG.
    static func frame(_ src: URL) async throws -> URL {
        let asset = AVURLAsset(url: src)
        let dur = try await asset.load(.duration)
        let gen = AVAssetImageGenerator(asset: asset)
        gen.appliesPreferredTrackTransform = true
        gen.requestedTimeToleranceBefore = .zero
        gen.requestedTimeToleranceAfter = CMTime(seconds: 1, preferredTimescale: 600)
        let (img, _) = try await gen.image(at: CMTimeMultiplyByRatio(dur, multiplier: 1, divisor: 3))
        let out = try Files.dest(src, ext: "png", suffix: "frame")
        try Images.write(img, to: out, ext: "png", quality: 1, meta: [:])
        return out
    }

    /// Video tracks only, copied without re-encoding when possible.
    static func mute(_ src: URL) async throws -> URL {
        let asset = AVURLAsset(url: src)
        let dur = try await asset.load(.duration)
        let comp = AVMutableComposition()
        for t in try await asset.loadTracks(withMediaType: .video) {
            let ct = comp.addMutableTrack(withMediaType: .video, preferredTrackID: kCMPersistentTrackID_Invalid)
            try ct?.insertTimeRange(CMTimeRange(start: .zero, duration: dur), of: t, at: .zero)
            ct?.preferredTransform = try await t.load(.preferredTransform)
        }
        let ext = Catalog.norm(src.pathExtension) == "mov" ? "mov" : "mp4"
        let out = try Files.dest(src, ext: ext, suffix: "muted")
        do {
            try await export(comp, preset: AVAssetExportPresetPassthrough, to: out, type: ext == "mov" ? .mov : .mp4)
        } catch {
            try await export(comp, preset: AVAssetExportPresetHighestQuality, to: out, type: ext == "mov" ? .mov : .mp4)
        }
        return out
    }

    /// Twice as fast, sound included.
    static func double(_ src: URL) async throws -> URL {
        let asset = AVURLAsset(url: src)
        let dur = try await asset.load(.duration)
        let range = CMTimeRange(start: .zero, duration: dur)
        let comp = AVMutableComposition()
        for type in [AVMediaType.video, .audio] {
            for t in try await asset.loadTracks(withMediaType: type) {
                let ct = comp.addMutableTrack(withMediaType: type, preferredTrackID: kCMPersistentTrackID_Invalid)
                try ct?.insertTimeRange(range, of: t, at: .zero)
                if type == .video { ct?.preferredTransform = try await t.load(.preferredTransform) }
            }
        }
        comp.scaleTimeRange(range, toDuration: CMTimeMultiplyByRatio(dur, multiplier: 1, divisor: 2))
        let out = try Files.dest(src, ext: "mp4", suffix: "2x")
        try await export(comp, preset: AVAssetExportPresetHighestQuality, to: out, type: .mp4)
        return out
    }

    static func compressAudio(_ src: URL) async throws -> URL {
        let out = try Files.dest(src, ext: "m4a", suffix: "small")
        try await export(AVURLAsset(url: src), preset: AVAssetExportPresetAppleM4A, to: out, type: .m4a)
        return try keepIfSmaller(out, than: src)
    }
}
