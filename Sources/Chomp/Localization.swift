import Foundation

/// Chomp in other languages. Resources/Localization/strings.json maps each English string to its translations;
/// anything missing stays in English. `{0}`, `{1}`… stand for the values passed in.
enum Loc {
    static let table: [String: String] = {
        guard let url = Bundle.main.url(forResource: "strings", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let all = try? JSONSerialization.jsonObject(with: data) as? [String: [String: String]] else { return [:] }
        // "pt-BR" → pt-BR, then pt; "zh-Hans-CN" → zh-Hans-CN, zh-Hans, zh.
        for lang in Locale.preferredLanguages {
            let parts = lang.split(separator: "-").map(String.init)
            for n in stride(from: parts.count, through: 1, by: -1) {
                if let t = all[parts.prefix(n).joined(separator: "-")] { return t }
            }
            if parts.first == "en" { return [:] }
        }
        return [:]
    }()
}

func L(_ key: String, _ args: Any...) -> String {
    var s = Loc.table[key] ?? key
    for (i, a) in args.enumerated() { s = s.replacingOccurrences(of: "{\(i)}", with: "\(a)") }
    return s
}

/// "1 image", "3 images": each language words its own singular and plural.
func counted(_ n: Int, _ one: String, _ many: String) -> String {
    L(n == 1 ? "{0} \(one)" : "{0} \(many)", n)
}
