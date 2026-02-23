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
        public int CodaChance; // 0-100, percentage chance of appending a coda per syllable
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
        public static readonly CultureType[] All = { Finnish, Icelandic, Danish, Welsh, Gaelic, Brythonic };

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
                // Single vowels weighted 3x for shorter, more natural place names
                "a", "e", "i", "o", "u", "y", "ä", "ö",
                "a", "e", "i", "o", "u", "y", "ä", "ö",
                "a", "e", "i", "o", "u", "y", "ä", "ö",
                "aa", "ee", "oo", "uu",
                "ai", "ei", "oi", "au", "ie", "uo"
            },
            Codas = new[]
            {
                // Finnish syllables often end open (no coda); closed syllables use n, s, l, r, t
                "", "", "", "", "n", "s", "l", "r", "t", "k"
            },
            CodaChance = 35, // Finnish strongly favors open syllables
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
                "Kuningaskunta", "Suuriruhtinaskunta", "Ruhtinaskunta", "Tasavalta", "Valtakunta", "Herttuakunta"
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
            CodaChance = 55,
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
                // Single vowels weighted 2x for shorter, more natural place names
                "a", "e", "i", "o", "u", "y",
                "æ", "ø", "å",
                "a", "e", "i", "o", "u", "y",
                "aa", "ee",
                "aj", "ej", "øj",
                "ou"
            },
            Codas = new[]
            {
                // Danish words often end in -en, -er, -el, -et; also -d (soft d), -g, -k, -s
                "", "", "n", "r", "l", "s", "t", "d", "g", "k",
                "nd", "ng", "nk", "rd", "rk", "rn", "ld", "lk", "ls"
            },
            CodaChance = 50,
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

        public static CultureType Welsh => new CultureType
        {
            Name = "Welsh",
            LeadingOnsets = new[]
            {
                // Welsh has no j, k, q, v, x, z in native words
                // f=/v/, ff=/f/, dd=/ð/, ll=voiceless lateral, ch=/x/, rh=voiceless r
                "b", "c", "d", "f", "ff", "g", "h", "l", "ll", "m", "n", "p", "r", "rh", "s", "t", "w",
                "br", "cr", "cl", "dr", "gr", "tr", "gl", "gw", "cw", "ch", "th",
                "c", "g", "ll", "m", "t", "r" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Welsh medial consonants and characteristic digraphs
                "", "b", "c", "d", "f", "ff", "g", "h", "l", "ll", "m", "n", "p", "r", "rh", "s", "t", "w",
                "ch", "dd", "th", "ng", "nt", "nd", "rn", "rf", "lw", "nw", "rw"
            },
            Vowels = new[]
            {
                // Welsh has 7 vowels: a, e, i, o, u, w, y (w and y are full vowels)
                // Circumflex marks long vowels (â, ô, ŵ, ŷ); less common but authentic
                // Single vowels weighted 3x for natural place names
                "a", "e", "i", "o", "u", "w", "y",
                "a", "e", "i", "o", "u", "w", "y",
                "a", "e", "i", "o", "u", "w", "y",
                "â", "ô", "ŵ", "ŷ",
                "ae", "ai", "au", "aw", "ei", "eu", "ew", "oe", "ow", "wy"
            },
            Codas = new[]
            {
                // Welsh codas include distinctive digraphs (dd, ff, ll, ch, th)
                "", "", "n", "r", "l", "s", "t", "d",
                "th", "dd", "ff", "ch", "ll",
                "rn", "nt", "nd", "ng"
            },
            CodaChance = 50,
            RealmSuffixes = new[]
            {
                // From Welsh kingdoms: Gwynedd, Powys, Dyfed, Gwent, Morgannwg, Ceredigion
                "edd", "wys", "ed", "ent", "wg", "igion", "arth", "iog"
            },
            ProvinceSuffixes = new[]
            {
                // Welsh cantref/regional suffixes
                "lyn", "dwy", "ydd", "on", "wyd", "rog", "ach", "eg", "wch", "ith"
            },
            CountySuffixes = new[]
            {
                // Welsh settlement elements (tref=town, llan=church, coed=wood, glyn=valley, etc.)
                "dref", "llan", "wen", "goch", "fach", "fawr", "coed", "glyn", "pwll", "maes", "nant"
            },
            GovernmentForms = new[]
            {
                "Teyrnas", "Tywysogaeth", "Brenhinaeth", "Arglwyddiaeth", "Gwladwriaeth", "Dugiaeth"
            },
            DirectionalPrefixes = new[]
            {
                "Gogledd", "De", "Dwyrain", "Gorllewin", "Uchaf", "Isaf", "Mewnol", "Allanol"
            }
        };

        public static CultureType Gaelic => new CultureType
        {
            Name = "Gaelic",
            LeadingOnsets = new[]
            {
                // Irish/Scottish Gaelic: lenited consonants (bh=/v/, mh=/v/, fh=silent, dh/gh=/ɣ/)
                // Broad/slender distinction shapes consonant quality
                "b", "bh", "c", "ch", "d", "dh", "f", "g", "gh", "l", "m", "mh", "n", "p", "r", "s", "sh", "t", "th",
                "br", "cl", "cn", "cr", "dr", "fl", "fr", "gl", "gr", "sc", "sl", "sn", "sp", "sr", "st", "str", "tr",
                "c", "d", "m", "s", "t", "b" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Gaelic medial: lenited forms common between vowels, double consonants
                "", "", "bh", "ch", "dh", "gh", "mh", "th", "sh",
                "b", "c", "d", "f", "g", "l", "m", "n", "p", "r", "s", "t",
                "ll", "nn", "rr", "cc", "pp", "tt",
                "rc", "rd", "rg", "rl", "rm", "rn", "lb", "lc", "lg", "nd", "ng", "nt"
            },
            Vowels = new[]
            {
                // Gaelic has broad (a, o, u) and slender (e, i) vowels with fadas (á, é, í, ó, ú)
                // "Caol le caol, leathan le leathan" (slender with slender, broad with broad)
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "á", "é", "í", "ó", "ú",
                "ai", "ei", "oi", "ui", "ao",
                "ia", "ua", "ea", "io"
            },
            Codas = new[]
            {
                // Gaelic words often end open or with -n, -r, -l, -s, -dh, -gh (many silent in speech)
                "", "", "", "n", "r", "l", "s", "t", "g", "d",
                "dh", "gh", "th", "ch",
                "nn", "ll", "rn", "rt", "rd"
            },
            CodaChance = 40,
            RealmSuffixes = new[]
            {
                // From Gaelic kingdoms: Dál Riata, Connacht, Munster, Ulster, Ailech
                "acht", "ster", "ail", "agh", "ala", "ria", "dha", "inn"
            },
            ProvinceSuffixes = new[]
            {
                // Gaelic regional/geographic suffixes (Irish forms)
                "more", "beg", "ard", "inis", "loch", "gleann", "ros", "dún", "mhuir", "rath"
            },
            CountySuffixes = new[]
            {
                // Gaelic settlement elements (baile=town, cill=church, dún=fort, ard=height, rath=ring fort)
                "baile", "cill", "dún", "ard", "rath", "drum", "lis", "ach", "agh", "more", "doire"
            },
            GovernmentForms = new[]
            {
                "Ríocht", "Tuath", "Tiarnas", "Cúige", "Dúiche", "Flaith"
            },
            DirectionalPrefixes = new[]
            {
                "Tuaisceart", "Deisceart", "Oirthear", "Iarthar", "Uachtar", "Íochtar", "Lár", "Imeall"
            }
        };

        public static CultureType Brythonic => new CultureType
        {
            Name = "Brythonic",
            LeadingOnsets = new[]
            {
                // Breton/Cornish: similar to Welsh but with distinct French influence (Breton)
                // and more English-adjacent forms (Cornish). No ll or rh.
                "b", "c", "d", "f", "g", "h", "k", "l", "m", "n", "p", "r", "s", "t", "w",
                "br", "cr", "cl", "dr", "fr", "gl", "gr", "gw", "pr", "pl", "tr", "str", "sk",
                "k", "t", "m", "p", "g", "l" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Brythonic medial: simpler clusters than Welsh, more continental feel
                "", "", "b", "c", "d", "f", "g", "h", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z",
                "ll", "nn", "rr", "ss", "mm",
                "rc", "rd", "rg", "rn", "rv", "lv", "nk", "nt", "nd", "ng", "sk", "st"
            },
            Vowels = new[]
            {
                // Breton/Cornish vowels: a, e, i, o, u plus nasal vowels in Breton (ã, õ)
                // More continental feel than Welsh
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "ae", "ei", "ou", "eu", "oa", "an", "en", "on"
            },
            Codas = new[]
            {
                // Brythonic codas: Cornish/Breton endings, less exotic than Welsh
                "", "", "n", "r", "l", "s", "t", "k",
                "th", "nk", "nt", "rk",
                "rn", "rd", "rs", "ns"
            },
            CodaChance = 50,
            RealmSuffixes = new[]
            {
                // From Brythonic kingdoms: Kernow, Dumnonia, Rheged, Elmet, Lothian
                "ow", "onia", "ged", "met", "ian", "orn", "ek", "onn"
            },
            ProvinceSuffixes = new[]
            {
                // Brythonic regional suffixes (Cornish/Breton influenced)
                "eth", "ock", "ard", "ance", "eur", "enn", "oer", "anz", "mor", "ster"
            },
            CountySuffixes = new[]
            {
                // Brythonic settlement elements (tre=town, pen=head, pol=pool, lan=enclosure, ker=fort)
                "tre", "pen", "pol", "lan", "ker", "ros", "gor", "men", "cas", "bod", "porth"
            },
            GovernmentForms = new[]
            {
                "Rouantelezh", "Penndugelezh", "Tierniezh", "Kontell", "Brozh", "Dukiezh"
            },
            DirectionalPrefixes = new[]
            {
                "Hanternoz", "Kreisteiz", "Reter", "Kornôg", "Uhel", "Izel", "Kreiz", "Diavaez"
            }
        };
    }
}

