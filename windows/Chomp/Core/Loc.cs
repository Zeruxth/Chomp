using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Chomp;

/// <summary>Chomp in other languages. Resources/Localization/strings.json (shared with the Mac app) maps each English
/// string to its translations; anything missing stays in English. {0}, {1}… stand for the values passed in.</summary>
public static class Loc
{
    static readonly Dictionary<string, string> Table = Load();

    static Dictionary<string, string> Load()
    {
        try
        {
            using var s = typeof(Loc).Assembly.GetManifestResourceStream("strings.json");
            if (s == null) return new();
            var all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(s) ?? new();
            // "pt-BR" → pt-BR, then pt; "zh-Hans" for Chinese in its simplified script.
            for (var c = CultureInfo.CurrentUICulture; c != CultureInfo.InvariantCulture; c = c.Parent)
            {
                if (c.TwoLetterISOLanguageName == "en") return new();
                if (all.TryGetValue(c.Name, out var t)) return t;
                if (c.Name.StartsWith("zh") && c.Name is not ("zh-TW" or "zh-HK" or "zh-MO" or "zh-Hant") && all.TryGetValue("zh-Hans", out var zh)) return zh;
            }
        }
        catch { }
        return new();
    }

    public static string L(string key, params object[] args)
    {
        var s = Table.TryGetValue(key, out var t) ? t : key;
        for (var i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", Convert.ToString(args[i], CultureInfo.CurrentCulture));
        return s;
    }

    /// <summary>"1 image", "3 images": each language words its own singular and plural.</summary>
    public static string Counted(int n, string one, string many) => L(n == 1 ? "{0} " + one : "{0} " + many, n);
}
