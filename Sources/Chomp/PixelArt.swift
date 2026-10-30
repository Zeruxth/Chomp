import AppKit
import SwiftUI

// MARK: - Colours

extension Color {
    init(hex h: UInt32) {
        self.init(red: Double((h >> 16) & 255) / 255, green: Double((h >> 8) & 255) / 255, blue: Double(h & 255) / 255)
    }
}

/// Colours every skin shares, so text and outlines always read the same.
enum Pal {
    static let ink: UInt32 = 0x140A08
    static let mouth: UInt32 = 0x24101C
    static let tooth: UInt32 = 0xF6F1E6
    static let tongue: UInt32 = 0xEA4F86
    static let tongueDark: UInt32 = 0xA3285A
    static let disc: UInt32 = 0x1D1420
    static let segment: UInt32 = 0x2F2231
    static let segmentRim: UInt32 = 0x3D2D3F
    static let hole: UInt32 = 0x120C14
    static let panel: UInt32 = 0x241A26
    static let muted: UInt32 = 0xB3A6B8

    static let inkC = Color(hex: ink)
    static let toothC = Color(hex: tooth)
    static let panelC = Color(hex: panel)
    static let raisedC = Color(hex: segment)
    static let mutedC = Color(hex: muted)
    static let tongueC = Color(hex: tongue)
}

// MARK: - Skins

/// A chest skin recolours the wood, trim and eyes of Chomp and the accent everywhere.
enum Skin: String, CaseIterable, Identifiable {
    case oak, frost, slime, void, bone, royal

    var id: String { rawValue }
    var title: String { rawValue.capitalized }

    static let royalAt = 100

    var note: String {
        switch self {
        case .oak: return "Default"
        case .frost: return "Icy blue"
        case .slime: return "Swamp green"
        case .void: return "Dungeon purple"
        case .bone: return "Greyscale"
        case .royal: return "\(Skin.royalAt) chomps"
        }
    }

    /// woodDark, wood, woodLight, woodHighlight, woodShadow, gold, goldDark, eye, accent, accentDark
    private var hexes: [UInt32] {
        switch self {
        case .oak:   return [0x5E3820, 0x7F512D, 0xA06A3A, 0xC48A50, 0x43281A, 0xFFD45C, 0xC48D2A, 0x8FF6FF, 0xFFD45C, 0xC48D2A]
        case .frost: return [0x2B4A6B, 0x3E6A94, 0x5A8FBF, 0x8FC0E8, 0x1F334D, 0xE8F6FF, 0x9CC4DE, 0xFFD45C, 0x8FD8FF, 0x4F9CC8]
        case .slime: return [0x2B5A2A, 0x3F7A3A, 0x5FA34F, 0x8CCB6A, 0x1E3F1E, 0xD9F27A, 0x9BB83A, 0xFF7AD9, 0xB6F05A, 0x7FB02E]
        case .void:  return [0x352050, 0x4B2F6B, 0x6A4A94, 0x9273C0, 0x24163A, 0xC9A6FF, 0x8A6AC8, 0x8FF6FF, 0xB894FF, 0x7E5CC4]
        case .bone:  return [0x4A4A50, 0x6B6B70, 0x8E8E95, 0xB8B8C0, 0x333338, 0xF2F2F2, 0xA8A8B0, 0xFF5A5A, 0xEDEDED, 0xA8A8B0]
        case .royal: return [0x8F6118, 0xC48D2A, 0xE0AE45, 0xFFD45C, 0x6B4510, 0xFFF3B0, 0xE8C25A, 0xEA4F86, 0xFFE27A, 0xC9A23A]
        }
    }

    var woodDark: Color { Color(hex: hexes[0]) }
    var wood: Color { Color(hex: hexes[1]) }
    var woodLight: Color { Color(hex: hexes[2]) }
    var woodHighlight: Color { Color(hex: hexes[3]) }
    var woodShadow: Color { Color(hex: hexes[4]) }
    var gold: Color { Color(hex: hexes[5]) }
    var goldDark: Color { Color(hex: hexes[6]) }
    var eye: Color { Color(hex: hexes[7]) }
    var accent: Color { Color(hex: hexes[8]) }
    var accentDark: Color { Color(hex: hexes[9]) }
    var accentHex: UInt32 { hexes[8] }
    var accentDarkHex: UInt32 { hexes[9] }

    var locked: Bool { self == .royal && Stats.chomps < Skin.royalAt }

    /// The saved skin. Falls back from the old accent setting so nobody loses their colour.
    static var current: Skin {
        let d = UserDefaults.standard
        if let raw = d.string(forKey: "skin"), let s = Skin(rawValue: raw) { return s.locked ? .oak : s }
        switch d.string(forKey: "accent") {
        case "lime": return .slime
        case "grape": return .void
        case "ice": return .frost
        case "mono": return .bone
        default: return .oak
        }
    }
}

enum Stats {
    static var chomps: Int { UserDefaults.standard.integer(forKey: "chomps") }
    static func addChomp() { UserDefaults.standard.set(chomps + 1, forKey: "chomps") }
}

// MARK: - Type

/// Pixel fonts for short names, plain system text for everything people need to read closely.
enum Fonts {
    /// Slice names, toast titles, headers. Pixelify Sans, bundled in Resources/Fonts.
    static func display(_ size: CGFloat, _ weight: Font.Weight = .semibold) -> Font {
        guard Prefs.pixelText else {
            return .system(size: size * 0.9, weight: weight == .regular ? .medium : .bold, design: .rounded)
        }
        // Lighter than it sounds: heavier weights close the gap in "C" and it reads as "O".
        let wght: CGFloat = weight == .bold ? 600 : (weight == .regular ? 400 : 500)
        return pixel("Pixelify Sans", size, wght: wght)
    }

    /// Tiny all-caps labels: keycaps, section headers. Silkscreen.
    static func label(_ size: CGFloat) -> Font {
        Prefs.pixelText ? pixel("Silkscreen", size, wght: nil) : .system(size: size, weight: .bold, design: .rounded)
    }

    private static var cache: [String: Font] = [:]

    /// Ligatures off (Pixelify joins "fi" into one glyph, so "file" reads as "Ale"),
    /// and weight set on the font's own axis instead of letting SwiftUI fake-bold it.
    private static func pixel(_ family: String, _ size: CGFloat, wght: CGFloat?) -> Font {
        let key = "\(family)-\(size)-\(wght ?? 0)"
        if let hit = cache[key] { return hit }
        var attrs: [NSFontDescriptor.AttributeName: Any] = [
            .family: family,
            .featureSettings: [[NSFontDescriptor.FeatureKey.typeIdentifier: kLigaturesType,
                                NSFontDescriptor.FeatureKey.selectorIdentifier: kCommonLigaturesOffSelector]],
        ]
        if let wght {
            attrs[NSFontDescriptor.AttributeName(rawValue: kCTFontVariationAttribute as String)] = [0x7767_6874: wght]  // 'wght'
        }
        let ns = NSFont(descriptor: NSFontDescriptor(fontAttributes: attrs), size: size)
        let f = ns.map { Font($0) } ?? .system(size: size, weight: .semibold, design: .rounded)
        cache[key] = f
        return f
    }

    /// File names, sizes, paths, errors.
    static func body(_ size: CGFloat, _ weight: Font.Weight = .medium) -> Font {
        .system(size: size, weight: weight)
    }
}

// MARK: - Frames

/// A rectangle with its corner pixels knocked out.
struct Notched: Shape {
    var px: CGFloat = 2

    func path(in r: CGRect) -> Path {
        var p = Path()
        p.addLines([
            CGPoint(x: r.minX + px, y: r.minY), CGPoint(x: r.maxX - px, y: r.minY),
            CGPoint(x: r.maxX - px, y: r.minY + px), CGPoint(x: r.maxX, y: r.minY + px),
            CGPoint(x: r.maxX, y: r.maxY - px), CGPoint(x: r.maxX - px, y: r.maxY - px),
            CGPoint(x: r.maxX - px, y: r.maxY), CGPoint(x: r.minX + px, y: r.maxY),
            CGPoint(x: r.minX + px, y: r.maxY - px), CGPoint(x: r.minX, y: r.maxY - px),
            CGPoint(x: r.minX, y: r.minY + px), CGPoint(x: r.minX + px, y: r.minY + px),
        ])
        p.closeSubpath()
        return p
    }
}

/// Ink outline with notched corners, a solid fill and a hard drop shadow.
struct PixelBox: ViewModifier {
    var fill: Color
    var px: CGFloat = 2
    var shade = false      // darker strip along the bottom, for keycaps
    var shadow = true

    func body(content: Content) -> some View {
        content.background(
            ZStack {
                if shadow { Notched(px: px).fill(Color.black.opacity(0.45)).offset(y: px) }
                Notched(px: px).fill(Pal.inkC)
                Rectangle().fill(fill).padding(px)
                if shade {
                    VStack(spacing: 0) {
                        Spacer(minLength: 0)
                        Rectangle().fill(Color.black.opacity(0.3)).frame(height: px)
                    }
                    .padding(px)
                }
            }
        )
    }
}

extension View {
    func pixelBox(_ fill: Color, px: CGFloat = 2, shade: Bool = false, shadow: Bool = true) -> some View {
        modifier(PixelBox(fill: fill, px: px, shade: shade, shadow: shadow))
    }
}

/// An arcade-style key: pressed ones light up in the skin's accent.
struct Keycap<Label: View>: View {
    var on: Bool
    var skin: Skin
    @ViewBuilder var label: Label

    var body: some View {
        label
            .foregroundStyle(on ? Pal.inkC : Pal.toothC)
            .pixelBox(on ? skin.accent : Pal.raisedC, shade: true)
    }
}

// MARK: - Icons

/// 12×12 icons, drawn the same way as Chomp. `#` is a pixel.
enum PixelIcons {
    static let all: [String: [String]] = [
        "compress": ["#..........#", ".#........#.", "..#.#..#.#..", "...##..##...", "..###..###..", "", "", "..###..###..", "...##..##...", "..#.#..#.#..", ".#........#.", "#..........#"],
        "1080": ["", ".###....###.", ".#........#.", ".#........#.", "", "....####....", "....####....", "", ".#........#.", ".#........#.", ".###....###."],
        "half": ["", "...####.....", "..#....#....", ".#......#...", ".#.####.#...", ".#......#...", "..#....#....", "...####.#...", ".........#..", "..........#.", "...........#"],
        "cutout": ["#.#.#..#.#.#", "", "#....##....#", "....####....", "#...####...#", ".....##.....", "#...####...#", "...######...", "#..######..#", "", "#.#.#..#.#.#"],
        "clean": ["...........#", "..........#.", "...#####.#..", "..#.....#...", ".#..##.#.#..", "#..###.#..#.", ".#..#.#..#..", "..#..#..#...", "...##.##....", "....#.......", "...#........", "..#........."],
        "rotate": ["", "....####.#..", "...#....##..", "..#....###..", "..#.........", "..#.........", "..#......#..", "..#......#..", "...#....#...", "....####...."],
        "combine": [".######.....", ".#....#.....", ".#.######...", ".#.#....#...", ".#.#.##.#...", ".###....#...", "...#.##.#...", "...#....#...", "...######..."],
        "send": ["", "..........##", "........###.", "......####..", "....#####...", "..######....", ".#######....", "....###.....", "....##......", "....#......."],
        "frame": ["", "############", "#..........#", "#.......##.#", "#.......##.#", "#..........#", "#...#......#", "#..###...#.#", "#.#####.####", "############"],
        "mute": ["", "....#.......", "...##.......", ".####..#...#", ".####...#.#.", ".####....#..", ".####...#.#.", "...##..#...#", "....#......."],
        "speed": ["", ".#....#.....", "..#....#....", "...#....#...", "....#....#..", ".....#....#.", "....#....#..", "...#....#...", "..#....#....", ".#....#....."],
        "wave": [".....#......", ".....#......", ".....#..#...", "..#..#..#...", "..#..#..#..#", "#.#..#..#..#", "..#..#..#..#", "..#..#..#...", ".....#..#...", ".....#......", ".....#......"],
        "trim": ["", ".#.......#..", "#.#.....#...", ".#.#...#....", "....#.#.....", ".....#......", "....#.#.....", ".#.#...#....", "#.#.....#...", ".#.......#.."],
        "mono": ["", "...######...", "..#...####..", ".#....#####.", "#.....######", "#.....######", "#.....######", "#.....######", ".#....#####.", "..#...####..", "...######..."],
        "split": ["", "", "#####..#####", "#...#..#...#", "#...#..#...#", "#...#..#...#", "#...#..#...#", "#...#..#...#", "#####..#####"],
        "count": ["", "...#....#...", "...#....#...", "############", "...#....#...", "...#....#...", "...#....#...", "############", "...#....#...", "...#....#..."],
        "text": ["", "############", "############", ".....##.....", ".....##.....", ".....##.....", ".....##.....", ".....##.....", "....####...."],
        "airdrop": ["", "...######...", "..#......#..", ".#..####..#.", "#..#....#..#", "#.#..##..#.#", "...#.##.#...", ".....##.....", ".....##.....", "....####...."],
        "copy": ["....####....", ".###.##.###.", ".#........#.", ".#.######.#.", ".#........#.", ".#.######.#.", ".#........#.", ".#.####...#.", ".#........#.", ".##########."],
        "path": ["", ".......###..", "......#...#.", ".....#..#.#.", "....#..#..#.", "...#..#..#..", "..#..#..#...", ".#..#..#....", ".#.#..#.....", ".#...#......", "..###......."],
        "zip": ["", ".##########.", ".#........#.", ".##########.", ".#...##...#.", ".#...##...#.", ".#........#.", ".#........#.", ".##########."],
        "terminal": ["", "############", "#..........#", "#.#........#", "#..#.......#", "#...#......#", "#..#.......#", "#.#..####..#", "#..........#", "############"],
        "desktop": ["", "############", "#..........#", "#..........#", "#..........#", "#..........#", "############", ".....##.....", "...######..."],
        "lock": ["", "", "...####.....", "..#....#....", "..#....#....", ".########...", ".###..###...", ".###..###...", ".########..."],
    ]
}

/// A pixel icon. `scale` is points per pixel; keep it a whole number.
struct PixelIcon: View {
    let name: String
    var scale: CGFloat = 2
    var color: Color = Pal.toothC

    var body: some View {
        Canvas { ctx, _ in
            for (y, row) in (PixelIcons.all[name] ?? []).enumerated() {
                for (x, ch) in row.enumerated() where ch == "#" {
                    ctx.fill(Path(CGRect(x: CGFloat(x) * scale, y: CGFloat(y) * scale, width: scale, height: scale)),
                             with: .color(color))
                }
            }
        }
        .frame(width: 12 * scale, height: 12 * scale)
        .accessibilityHidden(true)
    }
}

// MARK: - Wheel art

/// Paints the wheel disc pixel by pixel on a 220×220 grid, shown at 2×.
/// Each pixel works out which slice it belongs to from its angle, so arcs come out stepped.
enum WheelArt {
    static let grid = 220
    static let scale: CGFloat = 2
    static let center = 110.0
    static let outer = 102.0
    static let inner = 40.0
    static let disc = 108.0

    private static var cache: [String: CGImage] = [:]

    /// After a drop the slices give way to a plain ring: a spinner while working, then a solid rim.
    enum Ring: Hashable { case spin(Int), full(UInt32) }
    static let spinSteps = 24

    static func image(count n: Int, hover: Int?, skin: Skin, ring: Ring? = nil) -> CGImage? {
        let key = "\(n)-\(hover ?? -9)-\(skin.rawValue)-\(String(describing: ring))"
        if let hit = cache[key] { return hit }
        if cache.count > 64 { cache.removeAll() }

        let accent = skin.accentHex, accentDark = skin.accentDarkHex
        var px = [UInt8](repeating: 0, count: grid * grid * 4)
        let w = n > 0 ? 2 * Double.pi / Double(n) : 0

        for y in 0..<grid {
            for x in 0..<grid {
                let dx = Double(x) + 0.5 - center, dy = Double(y) + 0.5 - center
                let r = (dx * dx + dy * dy).squareRoot()
                var c: UInt32? = nil
                if r <= disc { c = r > disc - 2 ? Pal.ink : Pal.disc }
                if r < inner { c = r > inner - 2 ? Pal.ink : (hover == -1 ? Pal.mouth : Pal.hole) }
                if let ring, r >= inner + 5, r <= outer - 5 {
                    // Band for the text, with a 4-pixel rim on its outer edge.
                    c = r > outer - 6 ? Pal.segmentRim : Pal.segment
                    if r > outer - 10 {
                        switch ring {
                        case .full(let col): c = col
                        case .spin(let step):
                            var a = atan2(dx, -dy)
                            if a < 0 { a += 2 * .pi }
                            let head = 2 * Double.pi * Double(step) / Double(spinSteps)
                            let behind = (head - a + 4 * .pi).truncatingRemainder(dividingBy: 2 * .pi)
                            if behind < .pi / 3 { c = behind < .pi / 9 ? accent : accentDark }
                        }
                    }
                } else if n > 0, r >= inner + 5 {
                    var a = atan2(dx, -dy)
                    if a < 0 { a += 2 * .pi }
                    let t = (a + w / 2).truncatingRemainder(dividingBy: 2 * .pi)
                    let i = Int(t / w) % n
                    let off = min(t.truncatingRemainder(dividingBy: w), w - t.truncatingRemainder(dividingBy: w))
                    let hot = hover == i
                    let out = hot ? outer : outer - 5
                    if r <= out, off * r > 2 {
                        if hot {
                            c = (r > out - 1 || r < inner + 6 || off * r < 3) ? accentDark : accent
                        } else {
                            c = r > out - 1 ? Pal.segmentRim : Pal.segment
                        }
                    }
                }
                if let c {
                    let k = (y * grid + x) * 4
                    px[k] = UInt8((c >> 16) & 255); px[k + 1] = UInt8((c >> 8) & 255); px[k + 2] = UInt8(c & 255); px[k + 3] = 255
                }
            }
        }

        guard let provider = CGDataProvider(data: Data(px) as CFData),
              let img = CGImage(width: grid, height: grid, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: grid * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                                provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else { return nil }
        cache[key] = img
        return img
    }

    // MARK: Compact disc (after a drop)

    /// The small disc shown while a job runs and when it's done: a big hole for Chomp and a thin ring.
    static let compactGrid = 130
    static let compactHole = 41.0       // grid pixels; Chomp at 4× fits inside
    static let compactBand = 58.0
    static let compactDisc = 64.0

    static func compact(ring: Ring, skin: Skin) -> CGImage? {
        let key = "compact-\(skin.rawValue)-\(String(describing: ring))"
        if let hit = cache[key] { return hit }
        let g = compactGrid, c0 = Double(g) / 2
        let accent = skin.accentHex, accentDark = skin.accentDarkHex
        var px = [UInt8](repeating: 0, count: g * g * 4)
        for y in 0..<g {
            for x in 0..<g {
                let dx = Double(x) + 0.5 - c0, dy = Double(y) + 0.5 - c0
                let r = (dx * dx + dy * dy).squareRoot()
                var c: UInt32? = nil
                if r <= compactDisc { c = r > compactDisc - 2 ? Pal.ink : Pal.disc }
                if r > compactHole + 2, r <= compactBand {
                    c = Pal.segment
                    switch ring {
                    case .full(let col): c = col
                    case .spin(let step):
                        var a = atan2(dx, -dy)
                        if a < 0 { a += 2 * .pi }
                        let head = 2 * Double.pi * Double(step) / Double(spinSteps)
                        let behind = (head - a + 4 * .pi).truncatingRemainder(dividingBy: 2 * .pi)
                        if behind < .pi / 2 { c = behind < .pi / 6 ? accent : accentDark }
                    }
                }
                if r <= compactHole + 2 { c = r > compactHole ? Pal.ink : Pal.hole }
                if let c {
                    let k = (y * g + x) * 4
                    px[k] = UInt8((c >> 16) & 255); px[k + 1] = UInt8((c >> 8) & 255); px[k + 2] = UInt8(c & 255); px[k + 3] = 255
                }
            }
        }
        guard let provider = CGDataProvider(data: Data(px) as CFData),
              let img = CGImage(width: g, height: g, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: g * 4,
                                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedLast.rawValue),
                                provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)
        else { return nil }
        cache[key] = img
        return img
    }
}
