import SwiftUI

/// What Chomp is doing right now.
enum MimicMood: Equatable {
    case idle            // closed, breathing, eyes glint now and then
    case peek(Int)       // lid cracked, eyes looking -1 left / 0 / 1 right
    case closed          // shut tight (hovering cancel)
    case chomp           // one-shot: paper falls in, big bite, crumbs
    case working         // chewing loop
    case happy           // done: grin, tongue, sparkles
}

/// Pixel-art treasure-chest mimic, 32×33 grid. Wood, trim and eyes come from the skin.
enum Mimic {
    enum Eyes { case none, glow, happy }
    enum Tint { case tooth, gold, paper }

    struct Bit {
        let x: Int, y: Int, w: Int, h: Int
        let tint: Tint
        init(_ x: Int, _ y: Int, _ w: Int = 1, _ h: Int = 1, _ tint: Tint = .tooth) {
            self.x = x; self.y = y; self.w = w; self.h = h; self.tint = tint
        }
    }

    struct Frame {
        var open = 0
        var eyes: Eyes = .none
        var look = 0
        var tongue = false
        var dx = 0
        var bits: [Bit] = []
    }

    // MARK: Fixed colours (same in every skin)

    static let ink = Pal.inkC
    static let mouth = Color(hex: Pal.mouth)
    static let tooth = Pal.toothC
    static let tongueColor = Pal.tongueC
    static let tongueDark = Color(hex: Pal.tongueDark)
    static let shadow = Color.black.opacity(0.35)
    static let paper = Color.white

    // MARK: Animations

    static let tick = 0.11

    static let idle: [Frame] = {
        let opens = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0]
        let dxs = [0, 0, 0, 1, 1, 0, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]
        return opens.indices.map { Frame(open: opens[$0], eyes: .glow, dx: dxs[$0]) }
    }()

    static let chomp: [Frame] = [
        Frame(open: 0, bits: [Bit(14, 2, 3, 4, .paper)]),
        Frame(open: 0, bits: [Bit(14, 5, 3, 4, .paper)]),
        Frame(open: 3, eyes: .glow, bits: [Bit(14, 6, 3, 4, .paper)]),
        Frame(open: 6, eyes: .glow),
        Frame(open: 8, eyes: .glow, tongue: true),
        Frame(open: 8, eyes: .glow, tongue: true),
        Frame(open: 3, eyes: .glow),
        Frame(open: 0, dx: 1, bits: [Bit(3, 16), Bit(28, 15), Bit(2, 13, 1, 1, .gold)]),
        Frame(open: 0, dx: -1, bits: [Bit(1, 13), Bit(30, 12), Bit(0, 10, 1, 1, .gold)]),
        Frame(open: 0, bits: [Bit(0, 10), Bit(31, 9)]),
    ]

    static let working: [Frame] = [0, 2, 4, 4, 2, 0, 0, 0].map { Frame(open: $0, eyes: .glow, dx: $0 == 0 ? 0 : ($0 == 4 ? 1 : -1)) }

    static let happy: [Frame] = (0..<8).map { i in
        let sparkle: [Bit] = i % 4 < 2
            ? [Bit(2, 6, 1, 1, .gold), Bit(1, 7, 3, 1, .gold), Bit(2, 8, 1, 1, .gold), Bit(28, 2, 1, 1, .gold)]
            : [Bit(29, 5, 1, 1, .gold), Bit(28, 6, 3, 1, .gold), Bit(29, 7, 1, 1, .gold), Bit(3, 3, 1, 1, .gold)]
        return Frame(open: i == 4 || i == 5 ? 6 : 5, eyes: .happy, tongue: true, bits: sparkle)
    }

    /// Lid cracked with eyes showing: used for skin thumbnails.
    static let portrait = Frame(open: 2, eyes: .glow)

    static func frame(_ mood: MimicMood, at t: Date, since: Date) -> Frame {
        let n = Int(t.timeIntervalSinceReferenceDate / tick)
        switch mood {
        case .idle: return idle[n % idle.count]
        case .closed: return Frame()
        case .peek(let look):
            return Frame(open: n % 30 == 0 ? 1 : 2, eyes: .glow, look: look)
        case .chomp:
            let i = max(0, Int(t.timeIntervalSince(since) / 0.075))
            return i < chomp.count ? chomp[i] : Frame()
        case .working: return working[n % working.count]
        case .happy: return happy[n % happy.count]
        }
    }

    // MARK: Drawing

    static func draw(_ f: Frame, skin k: Skin, in ctx: inout GraphicsContext, scale s: CGFloat) {
        func px(_ x: Int, _ y: Int, _ w: Int, _ h: Int, _ c: Color) {
            ctx.fill(Path(CGRect(x: CGFloat(x) * s, y: CGFloat(y) * s, width: CGFloat(w) * s, height: CGFloat(h) * s)),
                     with: .color(c))
        }
        let open = min(max(f.open, 0), 9)
        let bx = 4 + f.dx, bw = 24, by = 19, bh = 11

        px(6, 30, 20, 2, shadow)

        // Body
        px(bx, by, bw, bh, ink)
        px(bx + 1, by + 1, bw - 2, bh - 2, k.wood)
        var x = bx + 5
        while x < bx + bw - 2 { px(x, by + 1, 1, bh - 2, k.woodDark); x += 5 }
        px(bx + 1, by + bh - 2, bw - 2, 1, k.woodShadow)
        px(bx + 1, by + 1, 2, bh - 2, k.goldDark); px(bx + bw - 3, by + 1, 2, bh - 2, k.goldDark)
        px(bx + 1, by + 1, 2, 1, k.gold); px(bx + bw - 3, by + 1, 2, 1, k.gold)

        // Mouth
        if open > 0 {
            px(bx, by - open, bw, open, ink)
            px(bx + 1, by - open, bw - 2, open, mouth)
        }
        if open >= 3 {
            x = bx + 3
            while x < bx + bw - 4 {
                px(x, by - 1, 2, 1, tooth)
                if open >= 6 { px(x, by - 2, 1, 1, tooth) }
                x += 3
            }
        }

        // Lid
        let ly = by - open - 9
        px(bx + 1, ly, bw - 2, 1, ink)
        px(bx, ly + 1, bw, 8, ink)
        px(bx + 1, ly + 1, bw - 2, 7, k.woodLight)
        px(bx + 2, ly + 1, bw - 4, 1, k.woodHighlight)
        x = bx + 5
        while x < bx + bw - 2 { px(x, ly + 2, 1, 4, k.wood); x += 5 }
        px(bx + 1, ly + 6, bw - 2, 2, k.goldDark); px(bx + 1, ly + 6, bw - 2, 1, k.gold)
        px(bx + 1, ly + 1, 2, 5, k.goldDark); px(bx + bw - 3, ly + 1, 2, 5, k.goldDark)
        px(bx + 1, ly + 1, 2, 1, k.gold); px(bx + bw - 3, ly + 1, 2, 1, k.gold)
        if open >= 3 {
            x = bx + 2
            while x < bx + bw - 3 {
                px(x, ly + 9, 2, 1, tooth)
                if open >= 6 { px(x + 1, ly + 10, 1, 1, tooth) }
                x += 3
            }
        }

        // Lock
        px(bx + 10, by, 4, 4, ink)
        px(bx + 10, by, 4, 3, k.gold)
        px(bx + 11, by + 1, 2, 1, k.goldDark)
        px(bx + 11, by + 2, 2, 1, ink)

        // Eyes
        if f.eyes != .none && open >= 1 {
            let h = open >= 3 ? 2 : 1
            let ey = open >= 3 ? by - open + max(1, open / 2 - 1) : by - 1
            if f.eyes == .happy && open >= 3 {
                for (ex, ey2) in [(6, 1), (7, 0), (8, 1), (16, 1), (17, 0), (18, 1)] {
                    px(bx + ex, ey + ey2, 1, 1, k.eye)
                }
            } else {
                px(bx + 6 + f.look, ey, 2, h, k.eye)
                px(bx + 16 + f.look, ey, 2, h, k.eye)
            }
        }

        // Tongue
        if f.tongue && open >= 4 {
            px(bx + 15, by - 1, 4, 7, ink)
            px(bx + 16, by - 1, 2, 6, tongueColor)
            px(bx + 16, by + 4, 2, 1, tongueDark)
        }

        for b in f.bits {
            let c: Color
            switch b.tint {
            case .tooth: c = tooth
            case .gold: c = k.gold
            case .paper: c = paper
            }
            px(b.x, b.y, b.w, b.h, c)
        }
    }
}

/// Animated Chomp. `scale` is points per pixel (use whole numbers so pixels stay crisp).
struct MimicView: View {
    var mood: MimicMood
    var since: Date = .distantPast
    var scale: CGFloat = 2
    /// Overrides the saved skin (skin picker thumbnails).
    var skin: Skin? = nil

    @AppStorage("skin") private var skinRaw = Skin.current.rawValue

    var body: some View {
        let k = skin ?? Skin(rawValue: skinRaw) ?? .oak
        TimelineView(.periodic(from: .now, by: 0.04)) { timeline in
            Canvas { ctx, _ in
                Mimic.draw(Mimic.frame(mood, at: timeline.date, since: since), skin: k, in: &ctx, scale: scale)
            }
        }
        .frame(width: 32 * scale, height: 33 * scale)
        .accessibilityLabel("Chomp")
    }
}

/// A still Chomp with its eyes peeking out, for skin thumbnails.
struct MimicPortrait: View {
    var skin: Skin
    var scale: CGFloat = 1

    var body: some View {
        Canvas { ctx, _ in Mimic.draw(Mimic.portrait, skin: skin, in: &ctx, scale: scale) }
            .frame(width: 32 * scale, height: 33 * scale)
            .accessibilityHidden(true)
    }
}
