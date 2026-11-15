using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;

namespace SpotLight;

/// <summary>Every game item's name and icon, read once from the game's own data (in the background).</summary>
public static class ItemNames
{
    public sealed record Entry(uint Id, string Name, uint Icon, string Category);

    private static Dictionary<uint, Entry> byId = new();
    private static List<Entry> all = new();
    public static bool Ready { get; private set; }

    public static void Load() => Task.Run(() =>
    {
        try
        {
            var map = new Dictionary<uint, Entry>();
            foreach (var item in Plugin.GameData.GetExcelSheet<Item>())
            {
                var name = item.Name.ToString();
                if (name.Length == 0) continue;
                var cat = item.ItemUICategory.ValueNullable?.Name.ToString() ?? "";
                map[item.RowId] = new Entry(item.RowId, name, item.Icon, cat);
            }
            byId = map;
            all = map.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Ready = true;
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Couldn't read the game's item list."); }
    });

    public static Entry? Get(uint id) => byId.TryGetValue(Configuration.BaseId(id), out var e) ? e : null;

    public static string Name(uint id) => Get(id)?.Name ?? $"Item #{id}";

    /// <summary>Items whose name contains every typed word (up to a limit, so the list stays quick).</summary>
    public static List<Entry> Search(string text, int limit = 60)
    {
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new();
        return all.Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).Take(limit).ToList();
    }

    public static IDalamudTextureWrap? Icon(uint iconId)
    {
        if (iconId == 0) return null;
        try
        {
            if (Plugin.TextureProvider.TryGetFromGameIcon(new GameIconLookup(iconId), out var tex) && tex.TryGetWrap(out var wrap, out _))
                return wrap;
        }
        catch (Exception) { /* no such icon */ }
        return null;
    }
}
