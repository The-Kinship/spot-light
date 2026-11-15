using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace SpotLight;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>The important items, by game item ID (normal quality ID; HQ copies count too).</summary>
    public HashSet<uint> Important { get; set; } = new();

    // --- How the highlight looks ---
    public bool HighlightOn { get; set; } = true;
    /// <summary>Box color (bright yellow to start).</summary>
    public Vector4 Color { get; set; } = new(1f, 0.9f, 0.1f, 1f);
    /// <summary>Border thickness in pixels.</summary>
    public float Thickness { get; set; } = 5f;
    /// <summary>A solid corner flag on important items (a shape, not just a color).</summary>
    public bool Badge { get; set; } = true;
    /// <summary>The highlight gently brightens and dims.</summary>
    public bool Pulse { get; set; }
    /// <summary>Darken every other item so important ones stand out.</summary>
    public bool DimOthers { get; set; }

    // --- Notes ---
    /// <summary>Why an item is important, by item ID (e.g. "for the stuffed peppers recipe").</summary>
    public Dictionary<uint, string> Notes { get; set; } = new();
    /// <summary>Show the note when you mouse over the item.</summary>
    public bool ShowNotes { get; set; } = true;
    /// <summary>Note text size (1 = normal).</summary>
    public float NoteSize { get; set; } = 1.3f;

    public string NoteFor(uint id) => Notes.TryGetValue(BaseId(id), out var n) ? n : "";

    public void SetNote(uint id, string note)
    {
        id = BaseId(id);
        if (note.Trim().Length == 0) Notes.Remove(id);
        else Notes[id] = note;
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>The plain item ID: HQ and collectable copies share their normal item's ID.</summary>
    public static uint BaseId(uint id)
    {
        if (id >= 1_000_000 && id < 2_000_000) return id - 1_000_000;   // high quality
        if (id >= 500_000 && id < 1_000_000) return id - 500_000;       // collectable
        return id;
    }

    public bool IsImportant(uint id) => Important.Contains(BaseId(id));

    public void SetImportant(uint id, bool important)
    {
        var changed = important ? Important.Add(BaseId(id)) : Important.Remove(BaseId(id));
        if (changed) Save();
    }
}
