using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;

var pluginPath = Environment.GetEnvironmentVariable("YASOU_TEST_PLUGIN")
    ?? throw new Exception("YASOU_TEST_PLUGIN is required.");
var voicebankPath = Environment.GetEnvironmentVariable("YASOU_TEST_VOICEBANK")
    ?? throw new Exception("YASOU_TEST_VOICEBANK is required.");

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
AssemblyLoadContext.Default.Resolving += (_, assemblyName) => {
    var dependencyName = $"{assemblyName.Name}.dll";
    foreach (var directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(pluginPath) }) {
        if (string.IsNullOrEmpty(directory)) {
            continue;
        }
        var candidate = Path.Combine(directory, dependencyName);
        if (File.Exists(candidate)) {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(candidate));
        }
    }
    return null;
};
var assembly = Assembly.LoadFile(Path.GetFullPath(pluginPath));
var type = assembly.GetExportedTypes().Single(candidate =>
    !candidate.IsAbstract && candidate.IsSubclassOf(typeof(Phonemizer)) &&
    candidate.GetCustomAttribute<PhonemizerAttribute>()?.Tag == "EN VCCV2ARPA");
var phonemizer = PhonemizerFactory.Get(type)?.Create()
    ?? throw new Exception("External phonemizer factory creation failed.");

VoicebankLoader.IsTest = true;
var voicebank = new Voicebank {
    File = Path.Combine(voicebankPath, "character.txt"),
    BasePath = AppContext.BaseDirectory,
};
VoicebankLoader.LoadVoicebank(voicebank);
var singer = new ClassicSinger(voicebank);
singer.EnsureLoaded();

var project = new UProject();
Ustx.AddDefaultExpressions(project);
var track = project.tracks[0];
project.expressions.TryGetValue(Ustx.CLR, out var descriptor);
track.VoiceColorExp = descriptor!.Clone();
track.VoiceColorExp.options = singer.Subbanks.Select(bank => bank.Color).Distinct().ToArray();
track.VoiceColorExp.max = track.VoiceColorExp.options.Length - 1;
var timeAxis = new TimeAxis();
timeAxis.BuildSegments(project);

var notes = new[] {
    new Phonemizer.Note {
        lyric = "-hE",
        duration = 480,
        position = 240,
        tone = MusicMath.NameToTone("C4"),
        phonemeAttributes = new[] {
            new Phonemizer.PhonemeAttributes { index = 0, consonantStretchRatio = 1 },
        },
    },
};
var groups = new[] { notes };
phonemizer.SetSinger(singer);
phonemizer.SetTiming(timeAxis);
phonemizer.SetUp(groups, project, track);
var result = phonemizer.Process(notes, null, null, null, null, Array.Empty<Phonemizer.Note>());
var aliases = result.phonemes.Select(item => item.phoneme).ToArray();

if (aliases.Length == 0 || aliases.All(string.IsNullOrEmpty)) {
    throw new Exception("External DLL returned no converted aliases.");
}
if (!aliases.Any(alias => alias?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Any(token => token is "hh" or "iy") == true)) {
    throw new Exception($"External DLL did not convert Cz VCCV -hE: {string.Join(", ", aliases)}");
}

Console.WriteLine($"Loaded external type: {type.FullName}");
Console.WriteLine($"-hE -> {string.Join(", ", aliases)}");
Console.WriteLine("External DLL test passed.");
