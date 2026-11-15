using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace SpotLight;

/// <summary>The Spot Light window: your important items on the left, a search box to add more on the right.</summary>
public sealed class MainWindow : Window
{
    private static readonly Vector4 Gold = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);

    private string search = "";
    private string filter = "";

    public MainWindow() : base("Rafter Cat's Spot Light##SpotLightMain")
    {
        Size = new Vector2(640, 440);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 260), MaximumSize = new Vector2(3000, 3000) };
    }

    public override void Draw()
    {
        ImGui.TextColored(Dim, "Tip: right-click any item in your bags and choose \"Spot Light: mark as important\".");
        DrawStyle();
        ImGui.Spacing();

        var height = Math.Max(120f, ImGui.GetContentRegionAvail().Y);
        var half = (ImGui.GetContentRegionAvail().X - 8f) / 2f;

        // --- Left: your important items ---
        if (ImGui.BeginChild("##mine", new Vector2(half, height)))
        {
            var cfg = Plugin.Config;
            ImGui.TextColored(Gold, $"Important items ({cfg.Important.Count})");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##filter", ref filter, 60);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Type to find something in your list.");

            uint? remove = null;
            var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var id in cfg.Important.OrderBy(i => ItemNames.Name(i), StringComparer.OrdinalIgnoreCase))
            {
                var name = ItemNames.Name(id);
                if (!words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                ImGui.PushID((int)id);
                if (ImGui.SmallButton("Remove")) remove = id;
                ImGui.SameLine();
                ItemRow(id, name);
                ImGui.PopID();
            }
            if (cfg.Important.Count == 0) ImGui.TextColored(Dim, "Nothing yet. Add items from the search on the right,\nor right-click them in your bags.");
            if (remove is { } r) cfg.SetImportant(r, false);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // --- Right: search the game's items and add them ---
        if (ImGui.BeginChild("##search", new Vector2(0, height)))
        {
            ImGui.TextColored(Gold, "Add items");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##search", ref search, 60);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Type part of an item's name, e.g. \"tiger\" or \"gazelle hide\".");

            if (!ItemNames.Ready) ImGui.TextColored(Dim, "Loading the game's item list...");
            else if (search.Trim().Length < 2) ImGui.TextColored(Dim, "Type at least 2 letters.");
            else
            {
                var results = ItemNames.Search(search);
                if (results.Count == 0) ImGui.TextColored(Dim, "No items found.");
                foreach (var e in results)
                {
                    ImGui.PushID((int)e.Id);
                    var important = Plugin.Config.IsImportant(e.Id);
                    if (important)
                    {
                        if (ImGui.SmallButton("Added")) Plugin.Config.SetImportant(e.Id, false);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Already important. Click to remove it.");
                    }
                    else if (ImGui.SmallButton(" Add ")) Plugin.Config.SetImportant(e.Id, true);
                    ImGui.SameLine();
                    ItemRow(e.Id, e.Name, e.Category);
                    ImGui.PopID();
                }
                if (results.Count >= 60) ImGui.TextColored(Dim, "Showing the first 60. Type more to narrow it down.");
            }
        }
        ImGui.EndChild();
    }

    /// <summary>How the highlight looks: on/off, color, thickness, corner flag, pulse, dim the rest.</summary>
    private static void DrawStyle()
    {
        var cfg = Plugin.Config;
        var on = cfg.HighlightOn;
        if (ImGui.Checkbox("Show highlights in my inventory", ref on)) { cfg.HighlightOn = on; cfg.Save(); }
        if (on && Highlighter.ShownLastFrame > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Gold, $"({Highlighter.ShownLastFrame} highlighted right now)");
        }
        if (!ImGui.CollapsingHeader("Highlight style")) return;

        ImGui.TextColored(Dim, "Open your inventory (\"all bags\" layout) to see changes as you make them.");
        var color = cfg.Color;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.ColorEdit4("Color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar)) cfg.Color = color;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();
        ImGui.SameLine();
        if (ImGui.SmallButton("Yellow")) { cfg.Color = new Vector4(1f, 0.9f, 0.1f, 1f); cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("White")) { cfg.Color = new Vector4(1f, 1f, 1f, 1f); cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Cyan")) { cfg.Color = new Vector4(0.2f, 1f, 1f, 1f); cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Magenta")) { cfg.Color = new Vector4(1f, 0.25f, 1f, 1f); cfg.Save(); }

        var th = cfg.Thickness;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat("Border thickness", ref th, 2f, 10f, "%.0f")) cfg.Thickness = th;
        if (ImGui.IsItemDeactivatedAfterEdit()) cfg.Save();

        var badge = cfg.Badge;
        if (ImGui.Checkbox("Corner flag (a shape to spot, not just a color)", ref badge)) { cfg.Badge = badge; cfg.Save(); }
        var pulse = cfg.Pulse;
        if (ImGui.Checkbox("Pulse (slowly brightens and dims)", ref pulse)) { cfg.Pulse = pulse; cfg.Save(); }
        var dimOthers = cfg.DimOthers;
        if (ImGui.Checkbox("Darken everything else", ref dimOthers)) { cfg.DimOthers = dimOthers; cfg.Save(); }
    }

    /// <summary>An item's icon and name (and its category, dimmed).</summary>
    private static void ItemRow(uint id, string name, string category = "")
    {
        var size = ImGui.GetTextLineHeight() + 4f;
        var icon = ItemNames.Icon(ItemNames.Get(id)?.Icon ?? 0);
        if (icon != null) ImGui.Image(icon.Handle, new Vector2(size, size));
        else ImGui.Dummy(new Vector2(size, size));
        ImGui.SameLine();
        ImGui.TextUnformatted(name);
        if (category.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, "(" + category + ")");
        }
    }
}
