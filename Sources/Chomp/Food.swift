import SwiftUI

/// Snacks Chomp eats when you drop a file. One is picked at random each time.
/// 8×8 sprites; each letter is a colour from `Food.colors`, `.` is see-through.
struct Food {
    let name: String
    let rows: [String]
    let crumb: Character     // colour of the crumbs that fly out

    static let all: [Food] = [
        Food(name: "page", rows: [
            "kkkkkk..",
            "kwwwwkk.",
            "kwggwwwk",
            "kwwwwwwk",
            "kwggggwk",
            "kwwwwwwk",
            "kwggggwk",
            "kkkkkkkk",
        ], crumb: "w"),
        Food(name: "drumstick", rows: [
            "....kkk.",
            "...koonk",
            "..kooook",
            "..kooonk",
            ".kwkook.",
            "kwk.kk..",
            "kwwk....",
            ".kk.....",
        ], crumb: "o"),
        Food(name: "sponge", rows: [
            "kkkkkkkk",
            "kGGGGGGk",
            "kkkkkkkk",
            "kyyyoyyk",
            "kyoyyyyk",
            "kyyyyoyk",
            "kyoyyyyk",
            "kkkkkkkk",
        ], crumb: "y"),
        Food(name: "brick", rows: [
            "........",
            "kkkkkkkk",
            "krrgrrrk",
            "kggggggk",
            "krrrrgrk",
            "kRRRRRRk",
            "kkkkkkkk",
            "........",
        ], crumb: "r"),
        Food(name: "fish", rows: [
            "........",
            "..kkkk.k",
            ".kBBBBkB",
            "kBkBBBBk",
            "kBBBBBBk",
            ".kBBBBkB",
            "..kkkk.k",
            "........",
        ], crumb: "B"),
        Food(name: "cheese", rows: [
            "........",
            "......kk",
            "....kkyk",
            "..kkyyyk",
            "kkyyoyyk",
            "kyyyyyok",
            "kyoyyyyk",
            "kkkkkkkk",
        ], crumb: "y"),
        Food(name: "donut", rows: [
            "..kkkk..",
            ".kpypBk.",
            "kpBpppyk",
            "kpk..kpk",
            "knk..knk",
            "knnnnnnk",
            ".knnnnk.",
            "..kkkk..",
        ], crumb: "p"),
        Food(name: "sock", rows: [
            "...kkkk.",
            "...kwwk.",
            "...krrk.",
            "...kwwk.",
            "...krrk.",
            "kkkkwwk.",
            "kwwwwwk.",
            "kkkkkkk.",
        ], crumb: "r"),
        Food(name: "pizza", rows: [
            "kkkkkkkk",
            "knnnnnnk",
            ".kyryyk.",
            ".kyyyrk.",
            "..kryk..",
            "..kyyk..",
            "...kk...",
            "........",
        ], crumb: "y"),
        Food(name: "duck", rows: [
            "..kkk...",
            ".kyyyk..",
            ".kykyokk",
            ".kyyyook",
            "kkyyykk.",
            "kyyyyyyk",
            "kyyyyyyk",
            ".kkkkkk.",
        ], crumb: "y"),
    ]

    static let colors: [Character: Color] = [
        "k": Color(hex: Pal.ink),
        "w": Color(hex: 0xF6F1E6),
        "g": Color(hex: 0xA59AA8),
        "y": Color(hex: 0xFFD45C),
        "o": Color(hex: 0xE8913A),
        "n": Color(hex: 0xC9874A),
        "r": Color(hex: 0xC8463C),
        "R": Color(hex: 0x8A2A28),
        "p": Color(hex: 0xEA4F86),
        "G": Color(hex: 0x5FA34F),
        "B": Color(hex: 0x5A9BD8),
    ]

    static func random() -> Int { Int.random(in: 0..<all.count) }
}
