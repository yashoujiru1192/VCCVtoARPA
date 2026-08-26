using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using VccvToArpa;

static void Equal(string expected, string actual, string label) {
    if (expected != actual) {
        throw new Exception($"{label}: expected '{expected}', got '{actual}'");
    }
}

Equal("ah", VccvToArpaPhonemizer.ReduceLiteVowel("ah"), "ah primary vowel");
Equal("ah", VccvToArpaPhonemizer.ReduceLiteVowel("ax"), "ax normalization");
Equal("ae", VccvToArpaPhonemizer.ReduceLiteVowel("ae"), "unrelated vowel preservation");
Equal("I've", VccvToArpaPhonemizer.NormalizeSynthVLyric("'I've"), "SynthV glottal marker");
Equal("can't", VccvToArpaPhonemizer.NormalizeSynthVLyric("can't"), "contraction apostrophe");
Equal("", VccvToArpaPhonemizer.NormalizeSynthVLyric("'"), "glottal-only lyric");
Equal("b r iy", string.Join(' ', VccvToArpaPhonemizer.ParseSynthVPhonemeLyric(".b r iy")), "SynthV direct phonemes");
Equal("g ah", string.Join(' ', VccvToArpaPhonemizer.ParseSynthVPhonemeLyric(".g ah cl")), "SynthV closure omission");
Equal("R", string.Join(' ', VccvToArpaPhonemizer.ParseSynthVPhonemeLyric(".sil")), "SynthV silence");
Equal("hh iy", string.Join(' ', VccvToArpaPhonemizer.ParseVccvAliasToArpabet("-hE")), "VCCV start CV");
Equal("iy l", string.Join(' ', VccvToArpaPhonemizer.ParseVccvAliasToArpabet("E l")), "VCCV VC");
Equal("l ow", string.Join(' ', VccvToArpaPhonemizer.ParseVccvAliasToArpabet("lO")), "VCCV compact CV");
Equal("ae ng", string.Join(' ', VccvToArpaPhonemizer.ParseVccvAliasToArpabet("Ang")), "VCCV composite vowel");
Equal("aw t", string.Join(' ', VccvToArpaPhonemizer.ParseVccvAliasToArpabet("8 t")), "VCCV diphthong VC");

var type = typeof(VccvToArpaPhonemizer);
var attribute = type.GetCustomAttributesData()
    .Single(item => item.AttributeType.Name == "PhonemizerAttribute");
Equal("EN VCCV2ARPA", (string)attribute.ConstructorArguments[1].Value!, "phonemizer identifier");

const string resourceName = "VccvToArpa.vccv-to-arpa.template.yaml";
using var stream = type.Assembly.GetManifestResourceStream(resourceName)
    ?? throw new Exception("Embedded YAML template is missing.");
using var reader = new StreamReader(stream);
var yaml = reader.ReadToEnd();
if (!yaml.Contains("from: ax") || !yaml.Contains("to: [ah]")
        || !yaml.Contains("from: ah, to: aa")) {
    throw new Exception("Primary ah and aa fallback rules are missing from the embedded YAML.");
}

var g2p = new ArpabetPlusG2p();
foreach (var word in new[] { "cup", "about", "another" }) {
    var source = g2p.Query(word) ?? throw new Exception($"G2P returned null for {word}.");
    var reduced = source.Select(VccvToArpaPhonemizer.ReduceLiteVowel).ToArray();
    if (reduced.Contains("ax")) {
        throw new Exception($"ax normalization failed for {word}: {string.Join(' ', reduced)}");
    }
    Console.WriteLine($"{word}: {string.Join(' ', source)} -> primary {string.Join(' ', reduced)}");
}

var voicebankPath = Environment.GetEnvironmentVariable("YASOU_TEST_VOICEBANK");
if (!string.IsNullOrWhiteSpace(voicebankPath)) {
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var characterFile = Path.Combine(voicebankPath, "character.txt");
    if (!File.Exists(characterFile)) {
        throw new Exception($"Voicebank character.txt was not found: {characterFile}");
    }

    VoicebankLoader.IsTest = true;
    var voicebank = new Voicebank {
        File = characterFile,
        BasePath = AppContext.BaseDirectory,
    };
    VoicebankLoader.LoadVoicebank(voicebank);
    var singer = new ClassicSinger(voicebank);
    singer.EnsureLoaded();

    var project = new UProject();
    Ustx.AddDefaultExpressions(project);
    var track = project.tracks[0];
    project.expressions.TryGetValue(Ustx.CLR, out var voiceColorDescriptor);
    track.VoiceColorExp = voiceColorDescriptor!.Clone();
    track.VoiceColorExp.options = singer.Subbanks
        .Select(subbank => subbank.Color)
        .Distinct()
        .OrderBy(color => color)
        .ToArray();
    track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;
    var timeAxis = new TimeAxis();
    timeAxis.BuildSegments(project);

    var words = new[] { "cup", "about", "another" };
    var groups = words.Select((word, index) => new[] {
        new Phonemizer.Note {
            lyric = word,
            duration = 480,
            position = 240 + index * 480,
            tone = MusicMath.NameToTone("C4"),
            phonemeAttributes = new[] {
                new Phonemizer.PhonemeAttributes {
                    index = 0,
                    consonantStretchRatio = 1,
                },
            },
        },
    }).ToArray();

    // Keep Testing=false to exercise the same initialization path used by an
    // installed external DLL in OpenUtau.
    var phonemizer = new VccvToArpaPhonemizer();
    phonemizer.SetSinger(singer);
    phonemizer.SetTiming(timeAxis);
    phonemizer.SetUp(groups, project, track);

    var aliases = new List<string>();
    for (var i = 0; i < groups.Length; i++) {
        var result = phonemizer.Process(
            groups[i],
            i > 0 ? groups[i - 1][0] : null,
            i < groups.Length - 1 ? groups[i + 1][0] : null,
            i > 0 ? groups[i - 1][0] : null,
            i < groups.Length - 1 ? groups[i + 1][0] : null,
            i > 0 ? groups[i - 1] : Array.Empty<Phonemizer.Note>());
        aliases.AddRange(result.phonemes.Select(item => item.phoneme));
    }
    if (aliases.Count == 0) {
        throw new Exception("Voicebank integration test returned no aliases.");
    }
    if (aliases.Any(alias => alias.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token == "ax"))) {
        throw new Exception($"Voicebank integration emitted ax: {string.Join(", ", aliases)}");
    }
    if (!aliases.Any(alias => alias.Contains("k ah", StringComparison.Ordinal)
            || alias.Contains("k aa", StringComparison.Ordinal))) {
        throw new Exception($"Central-vowel CV alias was not resolved: {string.Join(", ", aliases)}");
    }
    Console.WriteLine($"Voicebank integration aliases: {string.Join(", ", aliases)}");

    Phonemizer.Note MakeNote(string lyric, int position, int duration) => new() {
        lyric = lyric,
        position = position,
        duration = duration,
        tone = MusicMath.NameToTone("G3"),
        phonemeAttributes = new[] {
            new Phonemizer.PhonemeAttributes {
                index = 0,
                consonantStretchRatio = 1,
            },
        },
    };

    var outtaGroup = new[] {
        MakeNote("outta", 2000, 240),
        MakeNote("+", 2240, 240),
        MakeNote("+", 2480, 240),
    };
    var luckGroup = new[] {
        MakeNote("luck", 2720, 480),
    };
    var extensionGroups = new[] { outtaGroup, luckGroup };
    phonemizer.SetUp(extensionGroups, project, track);
    var outtaResult = phonemizer.Process(
        outtaGroup,
        null,
        luckGroup[0],
        null,
        luckGroup[0],
        Array.Empty<Phonemizer.Note>());
    if (outtaResult.phonemes.Any(item => item.phoneme
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token == "ax"))) {
        throw new Exception(
            $"Extender path emitted ax: " +
            string.Join(", ", outtaResult.phonemes.Select(item => $"{item.position}:{item.phoneme}")));
    }
    Console.WriteLine(
        $"outta + + stress-aware aliases: {string.Join(", ", outtaResult.phonemes.Select(item => $"{item.position}:{item.phoneme}"))}");

    Phonemizer.Result ProcessStandalone(Phonemizer.Note[] group) {
        phonemizer.SetUp(new[] { group }, project, track);
        return phonemizer.Process(
            group, null, null, null, null, Array.Empty<Phonemizer.Note>());
    }

    Phonemizer.Note MakeC4Note(string lyric, int position, int duration) => new() {
        lyric = lyric,
        position = position,
        duration = duration,
        tone = MusicMath.NameToTone("C4"),
        phonemeAttributes = new[] {
            new Phonemizer.PhonemeAttributes {
                index = 0,
                consonantStretchRatio = 1,
            },
        },
    };
    var primaryAhResult = ProcessStandalone(new[] { MakeC4Note(".ah", 5000, 480) });
    if (!primaryAhResult.phonemes.Any(item => item.phoneme.Contains("ah", StringComparison.Ordinal))
            || primaryAhResult.phonemes.Any(item => item.phoneme.Contains("aa", StringComparison.Ordinal))) {
        throw new Exception(
            $"Available ah alias was not preferred: " +
            string.Join(", ", primaryAhResult.phonemes.Select(item => item.phoneme)));
    }
    var fallbackAaResult = ProcessStandalone(new[] { MakeC4Note(".k ah", 5480, 480) });
    if (!fallbackAaResult.phonemes.Any(item => item.phoneme.Contains("k ah", StringComparison.Ordinal)
            || item.phoneme.Contains("k aa", StringComparison.Ordinal))) {
        throw new Exception(
            $"k ah alias was neither preserved nor resolved through aa fallback: " +
            string.Join(", ", fallbackAaResult.phonemes.Select(item => item.phoneme)));
    }
    Console.WriteLine(
        $"ah primary aliases: {string.Join(", ", primaryAhResult.phonemes.Select(item => item.phoneme))}");
    Console.WriteLine(
        $"ah singer-aware aliases: {string.Join(", ", fallbackAaResult.phonemes.Select(item => item.phoneme))}");

    var huntingPlain = new[] {
        MakeNote("hunting", 6000, 120),
        MakeNote("+", 6120, 240),
        MakeNote("+", 6360, 360),
    };
    var huntingTilde = new[] {
        MakeNote("hunting", 7000, 120),
        MakeNote("+~", 7120, 240),
        MakeNote("+", 7360, 360),
    };
    var huntingStar = new[] {
        MakeNote("hunting", 8000, 120),
        MakeNote("+*", 8120, 240),
        MakeNote("+", 8360, 360),
    };
    var huntingManualEarly = new[] {
        MakeNote("hunting", 8500, 120),
        MakeNote("+!", 8620, 240),
        MakeNote("+", 8860, 360),
    };
    var huntingManualLate = new[] {
        MakeNote("hunting", 9300, 120),
        MakeNote("+", 9420, 240),
        MakeNote("+!", 9660, 360),
    };
    var huntingManualMismatch = new[] {
        MakeNote("hunting", 10100, 120),
        MakeNote("+!", 10220, 240),
        MakeNote("+!", 10460, 360),
    };
    var huntingPlainResult = ProcessStandalone(huntingPlain);
    var huntingTildeResult = ProcessStandalone(huntingTilde);
    var huntingStarResult = ProcessStandalone(huntingStar);
    var huntingManualEarlyResult = ProcessStandalone(huntingManualEarly);
    var huntingManualLateResult = ProcessStandalone(huntingManualLate);
    var huntingManualMismatchResult = ProcessStandalone(huntingManualMismatch);
    string Signature(Phonemizer.Result result) => string.Join(", ",
        result.phonemes.Select(item => $"{item.position}:{item.phoneme}"));
    Equal(Signature(huntingPlainResult), Signature(huntingTildeResult), "hunting +~ tie alignment");
    Equal(Signature(huntingPlainResult), Signature(huntingStarResult), "hunting +* tie alignment");
    var huntingSecondVowel = huntingPlainResult.phonemes
        .FirstOrDefault(item => item.phoneme.Contains("t ih", StringComparison.Ordinal));
    if (string.IsNullOrEmpty(huntingSecondVowel.phoneme) || huntingSecondVowel.position < 300) {
        throw new Exception(
            $"hunting's second syllable was not aligned to the later note: {Signature(huntingPlainResult)}");
    }
    if (huntingTildeResult.phonemes.Any(item => item.position == 120
            && item.phoneme.StartsWith("aa", StringComparison.Ordinal)
            && !item.phoneme.Contains(' '))) {
        throw new Exception($"+~ emitted a vowel reattack: {Signature(huntingTildeResult)}");
    }
    var manualEarlySecondVowel = huntingManualEarlyResult.phonemes
        .FirstOrDefault(item => item.phoneme.Contains("t ih", StringComparison.Ordinal));
    var manualLateSecondVowel = huntingManualLateResult.phonemes
        .FirstOrDefault(item => item.phoneme.Contains("t ih", StringComparison.Ordinal));
    if (string.IsNullOrEmpty(manualEarlySecondVowel.phoneme)
            || manualEarlySecondVowel.position != 120) {
        throw new Exception(
            $"+! did not force hunting's second syllable to the earlier note: " +
            Signature(huntingManualEarlyResult));
    }
    if (string.IsNullOrEmpty(manualLateSecondVowel.phoneme)
            || manualLateSecondVowel.position != 360) {
        throw new Exception(
            $"+! did not force hunting's second syllable to the later note: " +
            Signature(huntingManualLateResult));
    }
    Equal(
        Signature(huntingPlainResult),
        Signature(huntingManualMismatchResult),
        "mismatched +! count automatic fallback");
    Console.WriteLine($"hunting + + aligned aliases: {Signature(huntingPlainResult)}");
    Console.WriteLine($"hunting +~ + tied aliases: {Signature(huntingTildeResult)}");
    Console.WriteLine($"hunting +! + manual-early aliases: {Signature(huntingManualEarlyResult)}");
    Console.WriteLine($"hunting + +! manual-late aliases: {Signature(huntingManualLateResult)}");

    // SynthV-like stress-aware placement: the unstressed ax in "about" should
    // consume the short pickup note, while the stressed aw receives the two
    // longer notes. Output aliases must still normalize ax to ah (or aa only
    // when the singer has no matching ah alias).
    var aboutStress = new[] {
        MakeNote("about", 9000, 120),
        MakeNote("+", 9120, 360),
        MakeNote("+", 9480, 240),
    };
    var aboutStressResult = ProcessStandalone(aboutStress);
    var aboutSecondVowel = aboutStressResult.phonemes
        .FirstOrDefault(item => item.phoneme.Contains("b aw", StringComparison.Ordinal));
    if (string.IsNullOrEmpty(aboutSecondVowel.phoneme) || aboutSecondVowel.position != 120) {
        throw new Exception(
            $"about's stressed syllable was not aligned after the short pickup: " +
            Signature(aboutStressResult));
    }
    if (aboutStressResult.phonemes.Any(item => item.phoneme
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token == "ax"))) {
        throw new Exception($"about emitted raw ax: {Signature(aboutStressResult)}");
    }
    Console.WriteLine($"about stress-aware aliases: {Signature(aboutStressResult)}");

    Phonemizer.Result[] ProcessSequence(params Phonemizer.Note[][] sequence) {
        phonemizer.SetUp(sequence, project, track);
        var results = new Phonemizer.Result[sequence.Length];
        for (var i = 0; i < sequence.Length; i++) {
            results[i] = phonemizer.Process(
                sequence[i],
                i > 0 ? sequence[i - 1][0] : null,
                i + 1 < sequence.Length ? sequence[i + 1][0] : null,
                i > 0 ? sequence[i - 1][0] : null,
                i + 1 < sequence.Length ? sequence[i + 1][0] : null,
                i > 0 ? sequence[i - 1] : Array.Empty<Phonemizer.Note>());
        }
        return results;
    }

    // A br note is an isolated boundary. This extracted Lite bank has no
    // literal br alias, so the breath itself must be silent while the words on
    // both sides keep independent endings/onsets.
    var breathSequence = ProcessSequence(
        new[] { MakeNote("about", 10000, 480) },
        new[] { MakeNote("br", 10480, 240) },
        new[] { MakeNote("distant", 10720, 480) });
    var hasLiteralBreath = singer.TryGetMappedOto("br", MusicMath.NameToTone("G3"), out _);
    if (hasLiteralBreath
            && (breathSequence[1].phonemes.Length != 1
                || breathSequence[1].phonemes[0].phoneme != "br")) {
        throw new Exception($"Available br alias was not selected: {Signature(breathSequence[1])}");
    }
    if (!hasLiteralBreath && breathSequence[1].phonemes.Length != 0) {
        throw new Exception($"Missing br alias emitted phonemes: {Signature(breathSequence[1])}");
    }
    if (!breathSequence[0].phonemes.Any(item => item.phoneme.Contains("t", StringComparison.Ordinal))) {
        throw new Exception($"Word before br lost its ending: {Signature(breathSequence[0])}");
    }
    if (breathSequence[2].phonemes.Any(item => item.phoneme.Contains("r d", StringComparison.Ordinal))) {
        throw new Exception($"br leaked /r/ into the next word: {Signature(breathSequence[2])}");
    }
    Console.WriteLine(
        $"about / br / distant: {Signature(breathSequence[0])} || " +
        $"{Signature(breathSequence[1])} || {Signature(breathSequence[2])}");

    // A short SVP-derived visual gap between identical word-boundary
    // consonants must keep one closure instead of releasing and attacking /k/
    // twice. A clearly longer rest must retain independent word boundaries.
    Phonemizer.Result[] ProcessSeparatedPair(
            Phonemizer.Note[] left, Phonemizer.Note[] right) {
        phonemizer.SetUp(new[] { left, right }, project, track);
        var leftResult = phonemizer.Process(
            left, null, right[0], null, null, Array.Empty<Phonemizer.Note>());
        var rightResult = phonemizer.Process(
            right, left[0], null, null, null, Array.Empty<Phonemizer.Note>());
        return new[] { leftResult, rightResult };
    }

    var shortLikeCome = ProcessSeparatedPair(
        new[] { MakeNote("like", 11200, 240) },
        new[] { MakeNote("come", 11680, 480) });
    var shortBoundaryAliases = shortLikeCome
        .SelectMany(result => result.phonemes)
        .Select(item => item.phoneme)
        .ToArray();
    if (!shortLikeCome[1].phonemes.Any(item =>
            item.phoneme.Contains("ay k", StringComparison.Ordinal))) {
        throw new Exception(
            $"Short like/come gap did not carry its shared /k/: " +
            $"{Signature(shortLikeCome[0])} || {Signature(shortLikeCome[1])}");
    }
    if (shortBoundaryAliases.Any(alias => alias == "k -" || alias == "- k")) {
        throw new Exception(
            $"Short like/come gap released and reattacked /k/: " +
            $"{Signature(shortLikeCome[0])} || {Signature(shortLikeCome[1])}");
    }

    var longLikeCome = ProcessSeparatedPair(
        new[] { MakeNote("like", 12500, 240) },
        new[] { MakeNote("come", 13220, 480) });
    if (!longLikeCome[1].phonemes.Any(item => item.phoneme == "- k")) {
        throw new Exception(
            $"Long like/come rest was incorrectly coalesced: " +
            $"{Signature(longLikeCome[0])} || {Signature(longLikeCome[1])}");
    }
    Console.WriteLine(
        $"short like ... come: {Signature(shortLikeCome[0])} || " +
        $"{Signature(shortLikeCome[1])}");
    Console.WriteLine(
        $"long like ... come: {Signature(longLikeCome[0])} || " +
        $"{Signature(longLikeCome[1])}");

    // Standalone SynthV consonants bridge adjacent words without becoming a
    // pseudo-vowel or causing the next CV to repeat its onset alias.
    var tBridge = ProcessSequence(
        new[] { MakeNote("heart", 12000, 480) },
        new[] { MakeNote(".t", 12480, 120) },
        new[] { MakeNote("tonight", 12600, 480) });
    if (tBridge[1].phonemes.Any(item => item.phoneme == "t t")
            || tBridge[2].phonemes.Any(item => item.phoneme == "- t")) {
        throw new Exception(
            $"Standalone .t duplicated its onset: {Signature(tBridge[1])} || {Signature(tBridge[2])}");
    }
    if (!tBridge[2].phonemes.Any(item => item.phoneme.Contains("t aa", StringComparison.Ordinal)
            || item.phoneme.Contains("t ah", StringComparison.Ordinal))) {
        throw new Exception($"Standalone .t did not carry into tonight: {Signature(tBridge[2])}");
    }

    var bBridge = ProcessSequence(
        new[] { MakeNote("abracadabra", 13500, 480) },
        new[] { MakeNote(".b", 13980, 120), MakeNote("+", 14100, 240) },
        new[] { MakeNote("'a", 14340, 480) });
    if (!bBridge[2].phonemes.Any(item => item.phoneme.Contains("b aa", StringComparison.Ordinal)
            || item.phoneme.Contains("b ah", StringComparison.Ordinal))) {
        throw new Exception($"Standalone .b did not carry into 'a: {Signature(bBridge[2])}");
    }
    if (bBridge[2].phonemes.Any(item => item.phoneme == "- b")) {
        throw new Exception($"Standalone .b repeated a starting alias: {Signature(bBridge[2])}");
    }
    Console.WriteLine(
        $"heart / .t / tonight: {Signature(tBridge[1])} || {Signature(tBridge[2])}");
    Console.WriteLine(
        $"abracadabra / .b+ / 'a: {Signature(bBridge[1])} || {Signature(bBridge[2])}");

    // A pre-phonemized Cz-style VCCV UST is already split into local CV/VC
    // aliases. Each note must be translated to ARPAsing independently rather
    // than being sent through English G2P or linked as another whole word.
    var vccvSequence = ProcessSequence(
        new[] { MakeNote("-hE", 14820, 360) },
        new[] { MakeNote("E l", 15180, 120) },
        new[] { MakeNote("lO", 15300, 360) },
        new[] { MakeNote("O -", 15660, 120) });
    var vccvAliases = vccvSequence
        .SelectMany(item => item.phonemes)
        .Select(item => item.phoneme)
        .ToArray();
    if (vccvAliases.Length == 0
            || vccvAliases.Any(alias => alias is "error" or "word not found")
            || vccvAliases.Any(alias => alias.IndexOfAny(
                new[] { '@', '0', '3', '6', '8', '9', 'E', 'I', 'O', 'Q' }) >= 0)) {
        throw new Exception(
            $"VCCV sequence was not converted to ARPAsing aliases: " +
            string.Join(", ", vccvAliases));
    }
    if (!vccvAliases.Any(alias => alias.Contains("hh iy", StringComparison.Ordinal))
            || !vccvAliases.Any(alias => alias.Contains("l ow", StringComparison.Ordinal))) {
        throw new Exception(
            $"VCCV CV aliases were lost: {string.Join(", ", vccvAliases)}");
    }
    Console.WriteLine(
        $"VCCV -hE / E l / lO / O -: " +
        string.Join(" || ", vccvSequence.Select(Signature)));

    foreach (var synthVLyric in new[] {
            // Every non-silence direct-phoneme pattern found in the three
            // supplied SVP-derived USTX projects.
            ".aa m", ".aa r t s", ".ae n", ".ae n d", ".ae n dx",
            ".ah", ".ao", ".ax", ".ay m", ".ay z", ".b aa cl",
            ".b l ah", ".b l iy", ".b r iy", ".ch ax", ".ch uw",
            ".cl ae n d", ".cl aw", ".cl n", ".d ax", ".d ow n",
            ".dh ae", ".dh ih n", ".dx ih n", ".eh r", ".er",
            ".f ao", ".g aa cl", ".g r iy", ".hh", ".hh aa",
            ".hh ay m", ".ih cl", ".iy", ".k ah cl", ".k ih",
            ".k ih n", ".l", ".m", ".m aa", ".m ih", ".n",
            ".n aa cl", ".p ao", ".r iy", ".s", ".s ih n",
            ".t ax", ".t eh", ".t uw", ".th r eh", ".v ih n",
            ".w ah", ".y ax", ".y ax l",
            "'I've", "'tight", "'is", "'incomplete", "'if", "'and", "'i",
            "'life", "'n", "'the", "'aam", "'oh", "'ah", "'a", "'e",
            "'up", "'easy", "'I'm", "'I'll",
        }) {
        var svpGroup = new[] { MakeNote(synthVLyric, 4000, 480) };
        phonemizer.SetUp(new[] { svpGroup }, project, track);
        var svpResult = phonemizer.Process(
            svpGroup, null, null, null, null, Array.Empty<Phonemizer.Note>());
        if (svpResult.phonemes.Length == 0
                || svpResult.phonemes.Any(item => item.phoneme is "error" or "word not found")) {
            throw new Exception(
                $"SynthV lyric '{synthVLyric}' did not convert: " +
                string.Join(", ", svpResult.phonemes.Select(item => item.phoneme)));
        }
        Console.WriteLine(
            $"SynthV {synthVLyric} -> {string.Join(", ", svpResult.phonemes.Select(item => item.phoneme))}");
    }
}

Console.WriteLine("All VCCVtoARPA tests passed.");
