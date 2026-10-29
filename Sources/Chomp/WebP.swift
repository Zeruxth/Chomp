import CoreGraphics
import CWebP
import Foundation

/// Writes WebP with Google's libwebp, built into Chomp. Keeps transparency.
enum WebP {
    static func write(_ img: CGImage, to out: URL, quality: Double) throws {
        let w = img.width, h = img.height, stride = w * 4
        var px = [UInt8](repeating: 0, count: stride * h)
        let drawn: Bool = px.withUnsafeMutableBytes { buf in
            guard let ctx = CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: stride,
                                      space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
            return true
        }
        guard drawn else { throw ChompError.msg("Couldn't read image") }

        // CoreGraphics gives premultiplied alpha; libwebp wants straight alpha.
        for i in Swift.stride(from: 0, to: px.count, by: 4) {
            let a = Int(px[i + 3])
            if a == 0 || a == 255 { continue }
            for c in 0..<3 { px[i + c] = UInt8(min(255, (Int(px[i + c]) * 255 + a / 2) / a)) }
        }

        var output: UnsafeMutablePointer<UInt8>?
        let size = WebPEncodeRGBA(px, Int32(w), Int32(h), Int32(stride), Float(quality * 100), &output)
        guard size > 0, let output else { throw ChompError.msg("Couldn't save WEBP") }
        defer { WebPFree(output) }
        try Data(bytes: output, count: size).write(to: out)
    }
}
