using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace SpotLight;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>The important items, by game item ID (normal quality ID; HQ copies count too).</summary>
    public HashSet<uint> Important { get; set; } = new();

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
