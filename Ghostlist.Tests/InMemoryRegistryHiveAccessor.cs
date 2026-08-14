using Microsoft.Win32;
using Ghostlist.Core;

namespace Ghostlist.Tests;

public sealed class InMemoryRegistryHiveAccessor : IRegistryHiveAccessor
{
    private readonly Dictionary<string, InMemoryRegistryKey> roots = new(StringComparer.OrdinalIgnoreCase);

    public IRegistryKeyHandle? OpenKey(RegistryHive hive, RegistryView view, string path, bool writable = false)
    {
        var key = Root(hive, view);
        foreach (var segment in Split(path))
        {
            if (!key.Children.TryGetValue(segment, out var child)) return null;
            key = child;
        }
        return key;
    }

    public IRegistryKeyHandle CreateKey(RegistryHive hive, RegistryView view, string path)
    {
        var key = Root(hive, view);
        foreach (var segment in Split(path))
            key = (InMemoryRegistryKey)key.CreateSubKey(segment);
        return key;
    }

    public InMemoryRegistryKey Root(RegistryHive hive, RegistryView view)
    {
        var id = $"{hive}:{view}";
        if (!roots.TryGetValue(id, out var root)) roots[id] = root = new InMemoryRegistryKey(id);
        return root;
    }

    private static string[] Split(string path) => path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
}

public sealed class InMemoryRegistryKey(string name) : IRegistryKeyHandle
{
    public string Name { get; } = name;
    public Dictionary<string, InMemoryRegistryKey> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (RegistryValueKind Kind, object Value)> values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetValueNames() => values.Keys.ToList();
    public IReadOnlyList<string> GetSubKeyNames() => Children.Keys.ToList();
    public RegistryValueKind GetValueKind(string name) => values[name].Kind;
    public object? GetValue(string name) => values.TryGetValue(name, out var item) ? item.Value : null;
    public void SetValue(string name, object value, RegistryValueKind kind) => values[name] = (kind, value);
    public void Dispose() { }

    public IRegistryKeyHandle? OpenSubKey(string name, bool writable = false) =>
        Children.TryGetValue(name, out var child) ? child : null;

    public IRegistryKeyHandle CreateSubKey(string name)
    {
        if (!Children.TryGetValue(name, out var child)) Children[name] = child = new InMemoryRegistryKey(name);
        return child;
    }

    public void DeleteSubKeyTree(string name)
    {
        if (!Children.Remove(name)) throw new ArgumentException($"Alt anahtar yok: {name}");
    }

    public void DeleteValue(string name)
    {
        if (!values.Remove(name)) throw new ArgumentException($"Deger yok: {name}");
    }
}
