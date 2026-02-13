namespace PopGen.Core
{
    /// <summary>
    /// Immutable phonetic fragment tables defining a language family.
    /// </summary>
    public sealed class CultureType
    {
        public string Name;
        public string[] LeadingOnsets;
        public string[] MedialOnsets;
        public string[] Vowels;
        public string[] Codas;
        public string[] RealmSuffixes;
        public string[] ProvinceSuffixes;
        public string[] CountySuffixes;
        public string[] GovernmentForms;
        public string[] DirectionalPrefixes;
    }

    /// <summary>
    /// Registry of available culture types.
    /// </summary>
    public static class CultureTypes
    {
        public static readonly CultureType[] All = { Finnish, Icelandic, Danish };

        public static CultureType Finnish => new CultureType
        {
            Name = "Finnish",
            LeadingOnsets = new[]
            {
                // Finnish native words rarely start with consonant clusters
                // No voiced stops (b/d/g) in native vocabulary
                "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v",
                "k", "t", "s", "h", "p", "r" // weighted duplicates for frequency
            },
            MedialOnsets = new[]
            {
                // Finnish has gemination (doubled consonants) and simple medial clusters
                "", "", "", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v",
                "kk", "ll", "mm", "nn", "pp", "rr", "ss", "tt",
                "lk", "nk", "mp", "nt", "lt", "rt", "sk", "st", "rv", "lm", "ht"
            },
            Vowels = new[]
            {
                // Finnish has 8 vowels (a, e, i, o, u, y, ä, ö), long vowels, and 18 diphthongs
                "a", "e", "i", "o", "u", "y", "ä", "ö",
                "aa", "ee", "ii", "oo", "uu", "yy", "ää", "öö",
                "ai", "ei", "oi", "ui", "yi", "äi", "öi",
                "au", "eu", "iu", "ou",
                "ie", "uo", "yö",
                "äy", "öy"
            },
            Codas = new[]
            {
                // Finnish syllables often end open (no coda); closed syllables use n, s, l, r, t
                "", "", "", "", "n", "s", "l", "r", "t", "k"
            },
            RealmSuffixes = new[]
            {
                "maa", "valta", "la", "nia", "sta", "nne", "kka", "sto"
            },
            ProvinceSuffixes = new[]
            {
                "linna", "koski", "niemi", "saari", "lahti", "joki", "lampi", "ranta", "selkä", "harju"
            },
            CountySuffixes = new[]
            {
                "la", "lä", "sto", "kylä", "vaara", "mäki", "pelto", "järvi", "suo", "kangas", "aho"
            },
            GovernmentForms = new[]
            {
                "Kuningaskunta", "Suuriruhtinaskunta", "Ruhtinaskunta", "Tasavalta", "Valtakunta", "Herttua"
            },
            DirectionalPrefixes = new[]
            {
                "Pohjois", "Etelä", "Itä", "Länsi", "Ylä", "Ala", "Sisä", "Ulko"
            }
        };
        public static CultureType Icelandic => new CultureType
        {
            Name = "Icelandic",
            LeadingOnsets = new[]
            {
                // Old Norse heritage with voiceless sonorants (hl, hr, hn) and þ/ð
                "b", "br", "d", "dr", "f", "fl", "fr", "g", "gn", "gr",
                "h", "hl", "hn", "hr", "hv", "j",
                "k", "kl", "kr", "l", "m", "n", "r", "s", "sk", "sl", "sn", "sp", "st", "sv",
                "t", "tr", "v", "þ", "þr"
            },
            MedialOnsets = new[]
            {
                // Gemination, Norse clusters, ð/þ in medial position
                "", "", "b", "d", "ð", "f", "g", "h", "k", "l", "m", "n", "r", "s", "t", "v",
                "ff", "ll", "nn", "rr", "ss", "tt", "kk", "pp",
                "nd", "ng", "nk", "rn", "rk", "rð", "rl", "lk", "lf", "lg", "fl", "fn"
            },
            Vowels = new[]
            {
                // Icelandic has short/long vowels and accented forms (á, é, í, ó, ú, ý, æ, ö)
                "a", "e", "i", "o", "u", "y",
                "á", "é", "í", "ó", "ú", "ý", "æ", "ö",
                "au", "ei", "ey"
            },
            Codas = new[]
            {
                // Icelandic nouns characteristically end in -ur, -ir, -ar
                "", "r", "n", "ð", "l", "s", "t", "k",
                "ur", "ir", "ar",
                "rn", "rð", "nd", "ng", "ll", "nn", "rr"
            },
            RealmSuffixes = new[]
            {
                "land", "ríki", "veldi", "heim", "garður", "ey"
            },
            ProvinceSuffixes = new[]
            {
                "fjörður", "nes", "dalur", "vík", "eyja", "staður", "fell", "heiði", "á", "vatn"
            },
            CountySuffixes = new[]
            {
                "bær", "garður", "holt", "tunga", "strönd", "vellir", "múli", "höfn", "borg", "staður"
            },
            GovernmentForms = new[]
            {
                "Konungsríki", "Hertogadæmi", "Jarldæmi", "Lýðveldi", "Ríki", "Furstadæmi"
            },
            DirectionalPrefixes = new[]
            {
                "Norður", "Suður", "Austur", "Vestur", "Efri", "Neðri", "Innri", "Ytri"
            }
        };

        public static CultureType Danish => new CultureType
        {
            Name = "Danish",
            LeadingOnsets = new[]
            {
                // Danish has voiced/voiceless stops, affricates, and a few clusters
                // Stød (glottal stop) is prosodic, not written — phonetic flavor comes from vowels
                "b", "bl", "br", "d", "dr", "f", "fl", "fr", "g", "gl", "gr", "gn",
                "h", "hj", "hv", "j", "k", "kl", "kn", "kr", "kv",
                "l", "m", "n", "p", "pl", "pr", "r", "s", "sk", "sl", "sm", "sn", "sp", "st", "sv",
                "t", "tr", "v"
            },
            MedialOnsets = new[]
            {
                // Danish medial: simple consonants, geminates, common clusters
                "", "", "b", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v",
                "bb", "dd", "ff", "gg", "kk", "ll", "mm", "nn", "pp", "rr", "ss", "tt",
                "nd", "ng", "nk", "ld", "lk", "rn", "rk", "rd", "sk", "st", "ft", "gt"
            },
            Vowels = new[]
            {
                // Danish has one of the largest vowel inventories in the world (~20+ monophthongs)
                // Written Danish uses æ, ø, å plus many diphthongs
                "a", "e", "i", "o", "u", "y",
                "æ", "ø", "å",
                "aa", "ee", "oo",
                "aj", "ej", "øj",
                "av", "ev", "øv",
                "iu", "ou"
            },
            Codas = new[]
            {
                // Danish words often end in -en, -er, -el, -et; also -d (soft d), -g, -k, -s
                "", "", "n", "r", "l", "s", "t", "d", "g", "k",
                "nd", "ng", "nk", "rd", "rk", "rn", "ld", "lk", "ls"
            },
            RealmSuffixes = new[]
            {
                "mark", "land", "rige", "gård", "holm", "borg"
            },
            ProvinceSuffixes = new[]
            {
                "sund", "borg", "bro", "vig", "ø", "sted", "fjord", "dal", "havn", "bjerg"
            },
            CountySuffixes = new[]
            {
                "by", "lund", "løse", "rup", "lev", "toft", "høj", "ager", "skov", "eng", "bæk"
            },
            GovernmentForms = new[]
            {
                "Kongerige", "Hertugdømme", "Jarldømme", "Grevskab", "Rige", "Fyrstendømme"
            },
            DirectionalPrefixes = new[]
            {
                "Nord", "Syd", "Øst", "Vest", "Øvre", "Nedre", "Indre", "Ydre"
            }
        };
    }
}
