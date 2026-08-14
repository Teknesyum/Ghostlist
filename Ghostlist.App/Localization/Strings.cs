using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Ghostlist.App;

public sealed class Strings : INotifyPropertyChanged
{
    public const string Turkish = "tr";
    public const string English = "en";

    private static readonly string[] Languages = [Turkish, English];

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> tables = [];
    private IReadOnlyDictionary<string, string> active;

    public static Strings Current { get; } = new();

    private Strings()
    {
        foreach (var language in Languages) tables[language] = Load(language);
        Language = Turkish;
        active = tables[Turkish];
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string Language { get; private set; }

    public string this[string key] => Get(key);

    public void Use(string language)
    {
        var requested = tables.ContainsKey(language) ? language : Turkish;
        if (requested == Language) return;
        Language = requested;
        active = tables[requested];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (active.TryGetValue(key, out var text)) return text;
        return tables[Turkish].TryGetValue(key, out var fallback) ? fallback : key;
    }

    public string Format(string key, params (string Name, object Value)[] arguments)
    {
        var text = Get(key);
        foreach (var (name, value) in arguments) text = text.Replace($"{{{name}}}", value.ToString());
        return text;
    }

    public IReadOnlyList<string> MissingKeys()
    {
        var reference = tables[Turkish].Keys.ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var (language, table) in tables)
        {
            foreach (var key in reference.Where(x => !table.ContainsKey(x))) missing.Add($"{language}:{key}");
            foreach (var key in table.Keys.Where(x => !reference.Contains(x))) missing.Add($"{language}:{key}");
        }
        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        var name = $"Ghostlist.App.Localization.{language}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Dil kaynağı bulunamadı: {name}");
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? throw new InvalidOperationException($"Dil kaynağı okunamadı: {name}");
    }
}
