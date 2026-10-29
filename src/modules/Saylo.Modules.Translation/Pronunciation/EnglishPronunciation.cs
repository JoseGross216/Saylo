using System.Text;

namespace Saylo.Modules.Translation.Pronunciation;

internal sealed class EnglishPronunciation
{
    private readonly Dictionary<string, List<string>> _entries;
    // Explicitly admitted citation/reduction variants; lexical sense contrasts are still rejected.
    private static readonly HashSet<string> CitationVariants = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "but", "can", "could", "do", "does", "for", "from",
        "had", "has", "have", "he", "her", "him", "his", "i", "is", "it", "of", "or", "our", "she", "should",
        "some", "than", "that", "the", "their", "them", "there", "these", "they", "this", "those", "to", "us",
        "was", "we", "were", "will", "with", "would", "you", "your",
        "good", "hello", "information", "message", "received", "request", "today", "tomorrow", "schedule",
        "don't", "can't", "we're", "i'm", "we'll", "i'll", "isn't", "it's", "that's", "there's", "you're",
        "we've", "they're", "won't", "didn't", "couldn't", "wouldn't", "shouldn't",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven",
        "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
        "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    };
    private static readonly HashSet<string> Ambiguous = new(StringComparer.Ordinal)
    {
        "bass", "bow", "close", "content", "contract", "desert", "digest", "does", "dove", "entrance", "excuse",
        "house", "invalid", "lead", "live", "minute", "object", "present", "produce", "project", "rebel", "record",
        "refuse", "resume", "row", "separate", "subject", "tear", "use", "used", "wind", "wound"
    };
    private static readonly Dictionary<string, string> Consonants = new(StringComparer.Ordinal)
    {
        ["B"] = "b", ["CH"] = "tʃ", ["D"] = "d", ["DH"] = "ð", ["F"] = "f", ["G"] = "ɡ", ["HH"] = "h",
        ["JH"] = "dʒ", ["K"] = "k", ["L"] = "l", ["M"] = "m", ["N"] = "n", ["NG"] = "ŋ", ["P"] = "p",
        ["R"] = "ɹ", ["S"] = "s", ["SH"] = "ʃ", ["T"] = "t", ["TH"] = "θ", ["V"] = "v", ["W"] = "w",
        ["Y"] = "j", ["Z"] = "z", ["ZH"] = "ʒ"
    };
    private static readonly Dictionary<string, string> Vowels = new(StringComparer.Ordinal)
    {
        ["AA"] = "ɑ", ["AE"] = "æ", ["AH"] = "ʌ", ["AO"] = "ɔ", ["AW"] = "aʊ", ["AY"] = "aɪ", ["EH"] = "ɛ",
        ["ER"] = "ɝ", ["EY"] = "eɪ", ["IH"] = "ɪ", ["IY"] = "i", ["OW"] = "oʊ", ["OY"] = "ɔɪ", ["UH"] = "ʊ", ["UW"] = "u"
    };
    private static readonly HashSet<string> Onsets = new(StringComparer.Ordinal)
    {
        "p", "b", "t", "d", "k", "ɡ", "f", "v", "θ", "ð", "s", "z", "ʃ", "ʒ", "h", "m", "n", "l", "ɹ", "w", "j", "tʃ", "dʒ",
        "pɹ", "bɹ", "tɹ", "dɹ", "kɹ", "ɡɹ", "fɹ", "θɹ", "ʃɹ", "pl", "bl", "kl", "ɡl", "fl", "sl",
        "tw", "dw", "kw", "ɡw", "sw", "sp", "st", "sk", "sm", "sn", "pj", "bj", "kj", "ɡj", "fj", "vj", "mj", "nj", "hj",
        "spɹ", "stɹ", "skɹ", "spl", "skl", "skw", "stj", "skj", "spj"
    };

    private EnglishPronunciation(Dictionary<string, List<string>> entries) => _entries = entries;
    internal int WordCount => _entries.Count;

    internal static EnglishPronunciation Load(string path, CancellationToken token)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 or > 8_000_000)
            throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var lines = 0;
        while (reader.ReadLine() is { } raw)
        {
            token.ThrowIfCancellationRequested();
            if (++lines > 160_000 || raw.Length > 512)
                throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0)
                continue;
            var separator = line.IndexOf(' ');
            if (separator < 1)
                throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
            var word = line[..separator];
            var variant = word.IndexOf('(');
            if (variant >= 0)
                word = word[..variant];
            var phones = line[(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (word.Length > 100 || phones.Length is < 1 or > 100 || phones.Any(phone => !IsPhone(phone)))
                throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
            if (!entries.TryGetValue(word, out var pronunciations))
                entries.Add(word, pronunciations = []);
            var pronunciation = string.Join(' ', phones);
            if (!pronunciations.Contains(pronunciation, StringComparer.Ordinal))
                pronunciations.Add(pronunciation);
        }
        if (entries.Count == 0)
            throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
        return new(entries);
    }

    private static bool IsPhone(string phone) => Consonants.ContainsKey(phone) ||
        phone.Length >= 3 && phone[^1] is '0' or '1' or '2' && Vowels.ContainsKey(phone[..^1]);

    internal string Generate(IReadOnlyList<string> words, int index)
    {
        var word = words[index].ToLowerInvariant();
        if (!_entries.TryGetValue(word, out var variants))
            throw PronunciationFailure.Create("OutOfVocabulary");
        string selected;
        if (word == "address")
        {
            // This US citation form is usable for both the noun and verb, unlike many heteronyms.
            selected = variants.FirstOrDefault(value => value == "AH0 D R EH1 S")
                ?? throw PronunciationFailure.Create("AmbiguousPronunciation");
        }
        else if (word == "read")
        {
            // Only explicit adjacent auxiliaries are supported; bare read remains ambiguous.
            var previous = index == 0 ? "" : words[index - 1].ToLowerInvariant();
            var vowel = previous is "to" or "will" or "can" or "please" or "must" or "should" ? "IY1"
                : previous is "have" or "has" or "had" ? "EH1" : null;
            selected = vowel is null ? throw PronunciationFailure.Create("AmbiguousPronunciation")
                : variants.FirstOrDefault(value => value.Split(' ').Contains(vowel, StringComparer.Ordinal))
                    ?? throw PronunciationFailure.Create("AmbiguousPronunciation");
        }
        else
        {
            if (Ambiguous.Contains(word) || variants.Count > 1 && !CitationVariants.Contains(word))
                throw PronunciationFailure.Create("AmbiguousPronunciation");
            selected = variants[0];
        }
        return ToIpa(selected);
    }

    internal static string ToIpa(string pronunciation)
    {
        var phones = pronunciation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (phones.Length == 0 || phones.Any(phone => !IsPhone(phone)))
            throw PronunciationFailure.Create("DictionaryFormat", Contracts.TranslationErrorCode.ModelInvalid);
        var vowels = Enumerable.Range(0, phones.Length).Where(index => char.IsDigit(phones[index][^1])).ToArray();
        if (vowels.Length == 0)
            throw PronunciationFailure.Create("NoSyllable");
        var mapped = phones.Select(phone => char.IsDigit(phone[^1])
            ? phone is "AH0" ? "ə" : phone is "ER0" ? "ɚ" : Vowels[phone[..^1]] : Consonants[phone]).ToArray();
        var stresses = new Dictionary<int, string>();
        for (var syllable = 0; syllable < vowels.Length; syllable++)
        {
            var vowel = vowels[syllable];
            var start = syllable == 0 ? 0 : vowel;
            if (syllable > 0)
                for (var candidate = vowels[syllable - 1] + 1; candidate < vowel; candidate++)
                    if (Onsets.Contains(string.Concat(mapped[candidate..vowel]))) { start = candidate; break; }
            if (phones[vowel][^1] is '1' or '2')
                stresses[start] = phones[vowel][^1] == '1' ? "ˈ" : "ˌ";
        }
        return string.Concat(mapped.Select((phone, index) => stresses.GetValueOrDefault(index, "") + phone));
    }
}
