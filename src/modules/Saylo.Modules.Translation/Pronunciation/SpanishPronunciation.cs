using System.Reflection;
using System.Text;

namespace Saylo.Modules.Translation.Pronunciation;

/// <summary>Bounded lexical-domain G2P: broad seseante, yeista Spanish, not narrow connected speech.</summary>
internal sealed class SpanishPronunciation
{
    private readonly HashSet<string> _vocabulary;
    private sealed record Phone(string Text, bool Vowel = false, bool Accent = false, bool Closed = false);
    private static readonly HashSet<string> Clusters = new(StringComparer.Ordinal)
        { "pl", "bl", "tɾ", "dɾ", "kl", "ɡl", "fl", "pɾ", "bɾ", "kɾ", "ɡɾ", "fɾ" };

    internal SpanishPronunciation()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "Saylo.Modules.Translation.Pronunciation.spanish-vocabulary.txt")
            ?? throw PronunciationFailure.Create("SpanishVocabularyMissing", Contracts.TranslationErrorCode.ModelUnavailable);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _vocabulary = reader.ReadToEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
    }
    internal int WordCount => _vocabulary.Count;

    internal string Generate(string word)
    {
        word = word.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        if (!_vocabulary.Contains(word))
            throw PronunciationFailure.Create("OutOfVocabulary");
        if (word == "méxico")
            return "ˈmexiko";
        if (word.EndsWith("mente", StringComparison.Ordinal) && word.Length > 7)
            return GenerateCore(word[..^5]).Replace("ˈ", "ˌ", StringComparison.Ordinal) + "ˈmente";
        return GenerateCore(word);
    }

    private static string GenerateCore(string word)
    {
        var phones = new List<Phone>();
        bool Front(int index) => index < word.Length && "eiéí".Contains(word[index]);
        for (var index = 0; index < word.Length; index++)
        {
            var letter = word[index];
            var next = index + 1 < word.Length ? word[index + 1] : '\0';
            if ("aeiouáéíóúü".Contains(letter))
            {
                var vowel = letter switch { 'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' or 'ü' => 'u', _ => letter };
                phones.Add(new(vowel.ToString(), true, "áéíóú".Contains(letter), vowel is 'i' or 'u'));
                continue;
            }
            string value;
            switch (letter)
            {
                case 'h': continue;
                case 'c' when next == 'h': value = "tʃ"; index++; break;
                case 'c': value = Front(index + 1) ? "s" : "k"; break;
                case 'g' when next == 'u' && Front(index + 2): value = "ɡ"; index++; break;
                case 'g': value = Front(index + 1) ? "x" : "ɡ"; break;
                case 'q' when next == 'u' && Front(index + 2): value = "k"; index++; break;
                case 'j': value = "x"; break;
                case 'l' when next == 'l': value = "ʝ"; index++; break;
                case 'r' when next == 'r': value = "r"; index++; break;
                case 'r': value = index == 0 || "lns".Contains(word[index - 1]) ? "r" : "ɾ"; break;
                case 'b': case 'v': value = "b"; break;
                case 'z': value = "s"; break;
                case 'ñ': value = "ɲ"; break;
                case 'x': phones.Add(new("k")); value = "s"; break;
                case 'y' when word.Length == 1: phones.Add(new("i", true, false, true)); continue;
                case 'y' when index == word.Length - 1: value = "i̯"; break;
                case 'y': value = "ʝ"; break;
                case 'p': case 't': case 'd': case 'k': case 'f': case 's': case 'm': case 'n': case 'l': value = letter.ToString(); break;
                default: throw PronunciationFailure.Create("UnsupportedGrapheme");
            }
            phones.Add(new(value));
        }
        // A stressed closed vowel forces hiatus; adjacent open vowels remain separate nuclei.
        for (var index = 0; index + 1 < phones.Count; index++)
        {
            var left = phones[index];
            var right = phones[index + 1];
            if (!left.Vowel || !right.Vowel || left.Text == right.Text ||
                left.Closed && left.Accent || right.Closed && right.Accent || !left.Closed && !right.Closed)
                continue;
            if (left.Closed)
                phones[index] = new(left.Text == "i" ? "j" : "w");
            else
                phones[index + 1] = new(right.Text + "̯");
        }
        var nuclei = Enumerable.Range(0, phones.Count).Where(index => phones[index].Vowel).ToArray();
        if (nuclei.Length == 0)
            throw PronunciationFailure.Create("NoSyllable");
        var accent = Array.FindIndex(nuclei, index => phones[index].Accent);
        var syllable = accent >= 0 ? accent : nuclei.Length == 1 ? -1 :
            "aeiouáéíóúüns".Contains(word[^1]) ? nuclei.Length - 2 : nuclei.Length - 1;
        var start = -1;
        if (syllable >= 0)
        {
            start = syllable == 0 ? 0 : nuclei[syllable];
            if (syllable > 0)
                for (var candidate = nuclei[syllable - 1] + 1; candidate < nuclei[syllable]; candidate++)
                    if (IsOnset(phones.Skip(candidate).Take(nuclei[syllable] - candidate).Select(phone => phone.Text).ToArray()))
                    { start = candidate; break; }
        }
        return string.Concat(phones.Select((phone, index) => (index == start ? "ˈ" : "") + phone.Text));
    }

    private static bool IsOnset(string[] phones)
    {
        if (phones.Length == 0)
            return false;
        if (phones.Length == 1)
            return !phones[0].EndsWith("̯", StringComparison.Ordinal);
        if (phones[^1] is "j" or "w")
            return IsOnset(phones[..^1]);
        return Clusters.Contains(string.Concat(phones));
    }
}
