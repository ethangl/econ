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
        public static readonly CultureType[] All = { Finnish };

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
    }
}
