namespace Chomp;

/// <summary>Snacks Chomp eats when you drop a file, generated from Food.swift. 8×8; each letter is a colour, `.` is see-through.</summary>
public sealed record Food(string Name, string[] Rows, char Crumb)
{
    public static readonly Food[] All =
    {
        new("page", new[] { "kkkkkk..", "kwwwwkk.", "kwggwwwk", "kwwwwwwk", "kwggggwk", "kwwwwwwk", "kwggggwk", "kkkkkkkk" }, 'w'),
        new("drumstick", new[] { "....kkk.", "...koonk", "..kooook", "..kooonk", ".kwkook.", "kwk.kk..", "kwwk....", ".kk....." }, 'o'),
        new("sponge", new[] { "kkkkkkkk", "kGGGGGGk", "kkkkkkkk", "kyyyoyyk", "kyoyyyyk", "kyyyyoyk", "kyoyyyyk", "kkkkkkkk" }, 'y'),
        new("brick", new[] { "........", "kkkkkkkk", "krrgrrrk", "kggggggk", "krrrrgrk", "kRRRRRRk", "kkkkkkkk", "........" }, 'r'),
        new("fish", new[] { "........", "..kkkk.k", ".kBBBBkB", "kBkBBBBk", "kBBBBBBk", ".kBBBBkB", "..kkkk.k", "........" }, 'B'),
        new("cheese", new[] { "........", "......kk", "....kkyk", "..kkyyyk", "kkyyoyyk", "kyyyyyok", "kyoyyyyk", "kkkkkkkk" }, 'y'),
        new("donut", new[] { "..kkkk..", ".kpypBk.", "kpBpppyk", "kpk..kpk", "knk..knk", "knnnnnnk", ".knnnnk.", "..kkkk.." }, 'p'),
        new("sock", new[] { "...kkkk.", "...kwwk.", "...krrk.", "...kwwk.", "...krrk.", "kkkkwwk.", "kwwwwwk.", "kkkkkkk." }, 'r'),
        new("pizza", new[] { "kkkkkkkk", "knnnnnnk", ".kyryyk.", ".kyyyrk.", "..kryk..", "..kyyk..", "...kk...", "........" }, 'y'),
        new("duck", new[] { "..kkk...", ".kyyyk..", ".kykyokk", ".kyyyook", "kkyyykk.", "kyyyyyyk", "kyyyyyyk", ".kkkkkk." }, 'y'),
    };

    public static readonly System.Collections.Generic.Dictionary<char, uint> Colors = new()
    {
        ['k'] = Pal.Ink,
        ['w'] = 0xF6F1E6,
        ['g'] = 0xA59AA8,
        ['y'] = 0xFFD45C,
        ['o'] = 0xE8913A,
        ['n'] = 0xC9874A,
        ['r'] = 0xC8463C,
        ['R'] = 0x8A2A28,
        ['p'] = 0xEA4F86,
        ['G'] = 0x5FA34F,
        ['B'] = 0x5A9BD8,
    };

    public static int Random() => System.Random.Shared.Next(All.Length);
}
