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
        public static readonly CultureType[] All = { Finnish, Icelandic, Danish, Welsh, Gaelic, Brythonic, LowGerman, HighGerman, Frisian, WestSlavic, SouthSlavic, Baltic, Ugric };

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

        public static CultureType LowGerman => new CultureType
        {
            Name = "LowGerman",
            LeadingOnsets = new[]
            {
                // Dutch/Low German: voiced fricatives (v, z), "sch" cluster, "w" pronounced /ʋ/
                // No High German consonant shift (p stays p, t stays t)
                "b", "bl", "br", "d", "dr", "f", "fl", "fr", "g", "gr",
                "h", "j", "k", "kl", "kn", "kr", "l", "m", "n", "p", "pl", "pr",
                "r", "s", "sch", "sl", "sm", "sn", "sp", "st", "str", "sw",
                "t", "tr", "v", "w", "z", "zw",
                "b", "d", "h", "k", "s", "w" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Dutch/Low German medial consonants and clusters
                "", "", "b", "d", "f", "g", "k", "l", "m", "n", "p", "r", "s", "t", "v", "w", "z",
                "sch", "ch",
                "ll", "mm", "nn", "rr", "ss", "tt",
                "nd", "ng", "nk", "rk", "rd", "rn", "lk", "ld", "ft", "cht"
            },
            Vowels = new[]
            {
                // Dutch/Low German: "ij" diphthong, double vowels for length, "oe" for /u/
                // No ü/ö (those are High German); Dutch uses "oe", "eu" instead
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "aa", "ee", "oo",
                "ij", "oe", "ui", "ei", "ou", "eu", "ie"
            },
            Codas = new[]
            {
                // Dutch/Low German: frequent closed syllables, "cht" cluster
                "", "n", "r", "l", "s", "t", "k", "d", "g",
                "nd", "ng", "nk", "rk", "rd", "rn", "ld", "lk", "cht", "ft", "ts"
            },
            CodaChance = 60,
            RealmSuffixes = new[]
            {
                "land", "rijk", "burg", "heim", "mark", "stein", "gouw", "veld"
            },
            ProvinceSuffixes = new[]
            {
                "haven", "berg", "dijk", "veld", "meer", "woud", "daal", "horn", "brink", "wijk"
            },
            CountySuffixes = new[]
            {
                "dorp", "dam", "hem", "hoek", "waard", "veen", "beek", "drecht", "kerk", "hout", "broek"
            },
            GovernmentForms = new[]
            {
                "Koninkrijk", "Hertogdom", "Graafschap", "Vorstendom", "Rijk", "Markgraafschap"
            },
            DirectionalPrefixes = new[]
            {
                "Noord", "Zuid", "Oost", "West", "Boven", "Beneden", "Binnen", "Buiten"
            }
        };

        public static CultureType HighGerman => new CultureType
        {
            Name = "HighGerman",
            LeadingOnsets = new[]
            {
                // High German consonant shift: p→pf, t→z(ts), k→ch
                // Characteristic "sch" cluster and multi-consonant onsets (schl-, schm-, schn-, schr-, schw-)
                "b", "bl", "br", "d", "dr", "f", "fl", "fr", "g", "gl", "gr",
                "h", "j", "k", "kl", "kn", "kr", "l", "m", "n", "p", "pf", "pl", "pr",
                "r", "s", "sch", "schl", "schm", "schn", "schr", "schw", "sp", "spr", "st", "str",
                "t", "tr", "w", "z", "zw",
                "b", "g", "h", "k", "s", "w" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // High German medial: shifted consonants (pf, tz, ch), umlauts shape surrounding vowels
                "", "", "b", "ch", "ck", "d", "f", "g", "h", "k", "l", "m", "n", "p", "pf", "r",
                "s", "sch", "ss", "t", "tz", "v", "w", "z",
                "ff", "ll", "mm", "nn", "rr", "tt",
                "nd", "ng", "nk", "rn", "rk", "rd", "lk", "ld", "ft", "cht", "rb", "rg", "rm", "lm"
            },
            Vowels = new[]
            {
                // High German: umlauts (ä, ö, ü), diphthongs (ei, au, eu/äu)
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "ä", "ö", "ü",
                "ei", "au", "eu", "ie"
            },
            Codas = new[]
            {
                // High German: heavy codas with shifted consonants
                "", "n", "r", "l", "s", "t", "k", "ch",
                "ck", "pf", "tz", "ff",
                "nd", "ng", "nk", "rn", "rk", "rd", "ld", "lk", "cht", "ft", "rm", "rg", "lm"
            },
            CodaChance = 65,
            RealmSuffixes = new[]
            {
                "reich", "land", "burg", "stein", "mark", "wald", "gau", "heim"
            },
            ProvinceSuffixes = new[]
            {
                "berg", "burg", "feld", "tal", "wald", "stein", "bach", "brück", "furt", "au"
            },
            CountySuffixes = new[]
            {
                "dorf", "heim", "hausen", "stadt", "burg", "lingen", "ingen", "kirchen", "ach", "brunn", "stein"
            },
            GovernmentForms = new[]
            {
                "Königreich", "Herzogtum", "Grafschaft", "Fürstentum", "Reich", "Markgrafschaft"
            },
            DirectionalPrefixes = new[]
            {
                "Nord", "Süd", "Ost", "West", "Ober", "Nieder", "Inner", "Äußer"
            }
        };

        public static CultureType Frisian => new CultureType
        {
            Name = "Frisian",
            LeadingOnsets = new[]
            {
                // Frisian: retains "sk" where English shifted to "sh", "ts" onset,
                // close to Old English phonology, simpler clusters than Dutch/German
                "b", "bl", "br", "d", "dr", "f", "fl", "fr", "g", "gr",
                "h", "j", "k", "kl", "kn", "kr", "l", "m", "n", "p", "pl", "pr",
                "r", "s", "sk", "sl", "sm", "sn", "sp", "st", "str", "sw",
                "t", "tr", "ts", "w", "wr",
                "b", "f", "h", "s", "sk", "w" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Frisian medial: simpler than Dutch, some distinctive clusters
                "", "", "b", "d", "f", "g", "k", "l", "m", "n", "p", "r", "s", "t", "w",
                "sk", "ts",
                "ll", "mm", "nn", "rr", "ss", "tt",
                "nd", "ng", "nk", "rk", "rd", "rn", "lk", "ld", "ft"
            },
            Vowels = new[]
            {
                // Frisian: distinctive vowel breaking, circumflexed long vowels (â, ê, ô, û)
                // "ij" shared with Dutch, "ea"/"oa" are characteristic Frisian diphthongs
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "â", "ê", "î", "ô", "û",
                "ea", "oa", "ie", "ij", "ei", "au", "oe"
            },
            Codas = new[]
            {
                // Frisian: moderate coda frequency, similar to Dutch but simpler
                "", "", "n", "r", "l", "s", "t", "k", "d",
                "nd", "ng", "nk", "rk", "rd", "rn", "ld", "lk", "ft", "ts"
            },
            CodaChance = 55,
            RealmSuffixes = new[]
            {
                // Frisian realm/territory elements
                "lân", "ryk", "gea", "hiem", "wâld", "oard", "steat", "goa"
            },
            ProvinceSuffixes = new[]
            {
                // Frisian geographic features
                "gea", "mar", "wâld", "heide", "hoek", "fjild", "dyk", "sleat", "oer", "grûn"
            },
            CountySuffixes = new[]
            {
                // Frisian settlement elements (buorren=village, wier=mound, tsjerke=church, stins=stone house)
                "buorren", "wier", "gea", "werp", "hûs", "tsjerke", "stins", "wâld", "hiem", "fean", "poel"
            },
            GovernmentForms = new[]
            {
                "Keninkryk", "Hartochdom", "Greefskip", "Furstendom", "Ryk", "Gea"
            },
            DirectionalPrefixes = new[]
            {
                "Noard", "Súd", "East", "West", "Boppeste", "Underste", "Binne", "Bûten"
            }
        };

        public static CultureType WestSlavic => new CultureType
        {
            Name = "WestSlavic",
            LeadingOnsets = new[]
            {
                // Polish/Czech: rich consonant clusters, palatalized sibilants (sz, cz, rz/ř)
                // Distinctive initial clusters (prz-, trz-, strz-, szcz- in Polish, stř- in Czech)
                "b", "br", "c", "ch", "cz", "d", "dr", "f", "g", "gl", "gn", "gr",
                "h", "j", "k", "kl", "kr", "kw", "l", "m", "n", "p", "pl", "pr", "prz",
                "r", "rz", "s", "sl", "sm", "sn", "st", "str", "sw", "sz",
                "t", "tr", "w", "z", "zd", "zl",
                "k", "p", "s", "st", "w", "z" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // West Slavic medial: palatalized pairs, consonant clusters between vowels
                "", "", "b", "c", "ch", "cz", "d", "g", "h", "j", "k", "l", "m", "n", "p",
                "r", "rz", "s", "sz", "t", "w", "z",
                "sk", "st", "sz", "cz", "rz",
                "nn", "ll", "ss",
                "nd", "nk", "rn", "rk", "rd", "lk", "ld", "wn", "wk"
            },
            Vowels = new[]
            {
                // Polish: nasal vowels (ą, ę), "y" as /ɨ/, "ó" as /u/
                // Czech: háčky long vowels (á, é, í, ú), "ů" for historical long u
                "a", "e", "i", "o", "u", "y",
                "a", "e", "i", "o", "u", "y",
                "a", "e", "i", "o", "u", "y",
                "ó", "ą", "ę",
                "ie", "ow", "ej"
            },
            Codas = new[]
            {
                // West Slavic: many words end in consonants, common -ów, -ek, -sk endings
                "", "n", "r", "l", "s", "t", "k", "d", "w", "c", "sz", "cz",
                "nd", "nk", "rk", "sk", "st", "wk"
            },
            CodaChance = 60,
            RealmSuffixes = new[]
            {
                // From Slavic polities: Polska, Czechy, Morava, Śląsk, Łużyce
                "ska", "sko", "nia", "wia", "chy", "awa", "ovy", "icz"
            },
            ProvinceSuffixes = new[]
            {
                // West Slavic regional/geographic elements
                "ów", "owa", "ice", "sko", "any", "ina", "ory", "ary", "yce", "ava"
            },
            CountySuffixes = new[]
            {
                // West Slavic settlement elements (gród=fort, wieś=village, miasto=city, dwór=manor)
                "ów", "owa", "ice", "owo", "iec", "nik", "any", "ary", "sko", "ina", "gród"
            },
            GovernmentForms = new[]
            {
                "Królestwo", "Księstwo", "Hrabstwo", "Rzeczpospolita", "Państwo", "Marchia"
            },
            DirectionalPrefixes = new[]
            {
                "Północ", "Południe", "Wschód", "Zachód", "Górny", "Dolny", "Wielki", "Mały"
            }
        };

        public static CultureType SouthSlavic => new CultureType
        {
            Name = "SouthSlavic",
            LeadingOnsets = new[]
            {
                // Serbian/Croatian/Bulgarian: j-palatalizations, "dž" affricate, fewer clusters than West Slavic
                // More open syllable structure, vowel-rich compared to Polish
                "b", "bl", "br", "c", "d", "dr", "g", "gl", "gn", "gr",
                "h", "j", "k", "kl", "kr", "kv", "l", "lj", "m", "n", "nj", "p", "pl", "pr",
                "r", "s", "sl", "sm", "sn", "sr", "st", "str", "sv",
                "t", "tr", "v", "vl", "vr", "z", "zd", "zl", "zv",
                "b", "d", "g", "k", "m", "s", "v" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // South Slavic medial: j-palatalizations (lj, nj), simple intervocalic consonants
                "", "", "b", "c", "d", "g", "h", "j", "k", "l", "lj", "m", "n", "nj", "p",
                "r", "s", "t", "v", "z",
                "st", "sk", "sv", "zd", "zn",
                "nd", "nk", "rn", "rk", "rd", "vn", "vk"
            },
            Vowels = new[]
            {
                // South Slavic: 5 pure vowels (a, e, i, o, u), no nasal vowels
                // Simpler than West Slavic, "r" can be syllabic (Krk, Brno — but awkward for names)
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "ije", "je", "av", "ov"
            },
            Codas = new[]
            {
                // South Slavic: moderate codas, many words end in vowels or simple consonants
                "", "", "", "n", "r", "l", "s", "t", "k", "d", "v",
                "nj", "lj", "sk", "st", "vk"
            },
            CodaChance = 45,
            RealmSuffixes = new[]
            {
                // From South Slavic polities: Srbija, Hrvatska, Bosna, Raška, Zeta, Duklja
                "ija", "ska", "sna", "ška", "ava", "ina", "ova", "lja"
            },
            ProvinceSuffixes = new[]
            {
                // South Slavic regional elements
                "ina", "ovo", "ava", "ica", "ije", "evo", "ište", "lje", "ane", "nik"
            },
            CountySuffixes = new[]
            {
                // South Slavic settlement elements (grad=city, selo=village, polje=field, dol=valley)
                "grad", "ovo", "ica", "ina", "ane", "nik", "ište", "polje", "dol", "selo", "ac"
            },
            GovernmentForms = new[]
            {
                "Kraljevina", "Vojvodina", "Kneževina", "Despotovina", "Banovina", "Država"
            },
            DirectionalPrefixes = new[]
            {
                "Severno", "Južno", "Istočno", "Zapadno", "Gornje", "Donje", "Veliko", "Malo"
            }
        };

        public static CultureType Baltic => new CultureType
        {
            Name = "Baltic",
            LeadingOnsets = new[]
            {
                // Lithuanian/Latvian: archaic Indo-European character, palatalized consonants
                // Lithuanian retains PIE features lost elsewhere; Latvian has distinctive "mīkstinājums" (ķ, ģ, ļ, ņ)
                "b", "bl", "br", "d", "dr", "g", "gl", "gr",
                "j", "k", "kl", "kr", "l", "m", "n", "p", "pl", "pr",
                "r", "s", "sk", "sl", "sm", "sn", "sp", "st", "str", "sv",
                "t", "tr", "v", "z", "zv",
                "k", "l", "m", "p", "s", "t", "v" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Baltic medial: simple consonants, palatalized pairs, few heavy clusters
                "", "", "b", "d", "g", "j", "k", "l", "m", "n", "p", "r", "s", "t", "v", "z",
                "sk", "st", "zd", "zg",
                "ll", "nn", "ss", "tt",
                "nd", "nk", "rn", "rk", "rd", "lk", "ld", "vn"
            },
            Vowels = new[]
            {
                // Lithuanian: long vowels (ė, ū, ų, ą, ę), diphthongs (ai, ei, au, ie, uo)
                // Latvian: macron long vowels (ā, ē, ī, ū), diphthongs (ai, ei, au, ie)
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "ė", "ū", "ą",
                "ai", "ei", "au", "ie", "uo"
            },
            Codas = new[]
            {
                // Baltic: most native words end in vowels or -s, -is, -as, -us (Lithuanian declension endings)
                // Very few final consonant clusters
                "", "", "", "s", "n", "r", "l", "t", "d",
                "as", "is", "us", "es",
                "ns", "rs", "ls"
            },
            CodaChance = 50,
            RealmSuffixes = new[]
            {
                // From Baltic polities: Lietuva, Latvija, Žemaitija, Kuršas, Prūsija
                "uva", "ija", "aitė", "šas", "ava", "aičiai", "onia", "ūra"
            },
            ProvinceSuffixes = new[]
            {
                // Baltic regional/geographic elements
                "ija", "ava", "ėnai", "upis", "ežeris", "giria", "kalnas", "slėnis", "laukas", "miškas"
            },
            CountySuffixes = new[]
            {
                // Baltic settlement elements (miestas=city, kaimas=village, pilis=castle, bažnyčia=church)
                "iai", "ava", "ėnai", "iškės", "aičiai", "uva", "upis", "pilis", "kalnas", "alus", "iena"
            },
            GovernmentForms = new[]
            {
                "Karalystė", "Kunigaikštystė", "Grafystė", "Respublika", "Valstybė", "Žemė"
            },
            DirectionalPrefixes = new[]
            {
                "Šiaurės", "Pietų", "Rytų", "Vakarų", "Aukštasis", "Žemasis", "Didysis", "Mažasis"
            }
        };

        public static CultureType Ugric => new CultureType
        {
            Name = "Ugric",
            LeadingOnsets = new[]
            {
                // Hungarian/Ugric: almost no initial consonant clusters in native words
                // Distinctive digraphs: sz=/s/, s=/ʃ/, cs=/tʃ/, zs=/ʒ/, gy=/dʲ/, ny=/ɲ/, ty=/tʲ/
                "b", "cs", "d", "f", "g", "gy", "h", "j", "k", "l", "m", "n", "ny", "p", "r", "s", "sz", "t", "ty", "v", "z", "zs",
                "k", "m", "n", "s", "sz", "t", "v" // weighted for frequency
            },
            MedialOnsets = new[]
            {
                // Hungarian medial: geminates common, distinctive digraphs between vowels
                "", "", "b", "cs", "d", "f", "g", "gy", "h", "j", "k", "l", "m", "n", "ny", "p", "r", "s", "sz", "t", "ty", "v", "z", "zs",
                "bb", "dd", "gg", "kk", "ll", "mm", "nn", "pp", "rr", "ss", "tt",
                "nd", "ng", "nk", "rd", "rk", "rn", "lk", "ld", "gy", "ny"
            },
            Vowels = new[]
            {
                // Hungarian: strict front/back vowel harmony
                // Back: a, á, o, ó, u, ú — Front: e, é, ö, ő, ü, ű
                // Short/long pairs distinguished by accent
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "a", "e", "i", "o", "u",
                "á", "é", "í", "ó", "ú", "ö", "ő", "ü", "ű"
            },
            Codas = new[]
            {
                // Hungarian: moderate closed syllables, many words end in consonants
                // No final clusters in native words beyond simple consonants
                "", "", "n", "r", "l", "s", "sz", "t", "k", "d", "g", "ny",
                "nd", "nk", "rd", "rk", "lk"
            },
            CodaChance = 50,
            RealmSuffixes = new[]
            {
                // From Hungarian polities: Magyarország, Erdély, Pannónia
                "ország", "szág", "föld", "hon", "ély", "nia", "alom", "ség"
            },
            ProvinceSuffixes = new[]
            {
                // Hungarian regional/geographic elements
                "ság", "ség", "vidék", "megye", "alj", "köz", "hát", "mente", "mellék", "erdő"
            },
            CountySuffixes = new[]
            {
                // Hungarian settlement elements (vár=castle, város=city, falu=village, háza=house)
                "vár", "város", "falu", "háza", "hely", "szék", "telep", "puszta", "kert", "tanya", "lak"
            },
            GovernmentForms = new[]
            {
                "Királyság", "Fejedelemség", "Grófság", "Hercegség", "Birodalom", "Köztársaság"
            },
            DirectionalPrefixes = new[]
            {
                "Észak", "Dél", "Kelet", "Nyugat", "Felső", "Alsó", "Nagy", "Kis"
            }
        };
    }
}

