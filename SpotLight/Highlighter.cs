using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpotLight;

/// <summary>
/// Draws the highlights on top of the game's inventory window. Read only: it looks at where the slots are on screen
/// and which item sits in each one, then draws boxes over them. It never touches the items themselves.
/// </summary>
public static unsafe class Highlighter
{
    /// <summary>The "all bags open" inventory layout: four grids, one per bag.</summary>
    private static readonly (string Addon, GameInventoryType Bag)[] ExpandedGrids =
    {
        ("InventoryGrid0E", GameInventoryType.Inventory1),
        ("InventoryGrid1E", GameInventoryType.Inventory2),
        ("InventoryGrid2E", GameInventoryType.Inventory3),
        ("InventoryGrid3E", GameInventoryType.Inventory4),
    };

    /// <summary>Game windows that can pop up over the inventory; highlights under them are skipped so they stay readable.</summary>
    private static readonly string[] Covers = { "ItemDetail", "ContextMenu", "InventoryContextMenu", "ContextIconMenu" };

    /// <summary>For the preview in settings: how many important items are showing right now.</summary>
    public static int ShownLastFrame { get; private set; }

    public static void Draw()
    {
        ShownLastFrame = 0;
        var cfg = Plugin.Config;
        if (!cfg.HighlightOn || (cfg.Important.Count == 0 && !cfg.DimOthers)) return;
        try
        {
            var covers = CoverRects();
            var draw = ImGui.GetBackgroundDrawList();
            foreach (var (addonName, bag) in ExpandedGrids)
                DrawGrid(draw, addonName, bag, covers);
        }
        catch (Exception ex)
        {
            // Never let a drawing problem spam or break anything; just stop for this frame.
            Plugin.Log.Verbose(ex, "Spot Light drawing skipped this frame.");
        }
    }

    private static void DrawGrid(ImDrawListPtr draw, string addonName, GameInventoryType bag, List<(Vector2 Min, Vector2 Max)> covers)
    {
        var addon = Addon(addonName);
        if (addon == null || !addon->IsVisible) return;

        var slots = SlotRects(addon);
        if (slots.Count != 35) return;   // not what we expect (mid-load or a changed layout); draw nothing rather than the wrong thing

        var items = Plugin.GameInventory.GetInventoryItems(bag);
        var cfg = Plugin.Config;
        var color = cfg.Color;
        if (cfg.Pulse)
        {
            // Gently brightens and dims about once a second.
            var t = (float)(Math.Sin(Environment.TickCount64 / 1000.0 * Math.PI * 1.2) * 0.5 + 0.5);
            color.W *= 0.55f + 0.45f * t;
        }
        var col = ImGui.ColorConvertFloat4ToU32(color);
        var dark = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.85f));
        var dim = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));

        for (var i = 0; i < slots.Count && i < items.Length; i++)
        {
            var id = items[i].ItemId;
            if (id == 0) continue;
            var (min, max) = slots[i];
            if (covers.Any(c => Overlaps(c, min, max))) continue;

            if (!cfg.IsImportant(id))
            {
                if (cfg.DimOthers) draw.AddRectFilled(min, max, dim, 4f);
                continue;
            }

            ShownLastFrame++;
            var th = cfg.Thickness;
            // Dark outline under the colored one, so it stands out on any background.
            draw.AddRect(min - new Vector2(1, 1), max + new Vector2(1, 1), dark, 5f, ImDrawFlags.None, th + 2f);
            draw.AddRect(min, max, col, 4f, ImDrawFlags.None, th);

            if (cfg.Badge)
            {
                // A solid corner flag in the top-left: a shape to spot, not just a color.
                var size = (max.X - min.X) * 0.42f;
                var a = min;
                var b = min + new Vector2(size, 0);
                var c = min + new Vector2(0, size);
                draw.AddTriangleFilled(a - new Vector2(1, 1), b + new Vector2(2, -1), c + new Vector2(-1, 2), dark);
                draw.AddTriangleFilled(a, b, c, col);
            }
        }
    }

    /// <summary>The 35 slot boxes of one bag grid, in the bag's own order (left to right, top to bottom).</summary>
    private static List<(Vector2 Min, Vector2 Max)> SlotRects(AtkUnitBase* addon)
    {
        var list = new List<(Vector2 Min, Vector2 Max, float Y, float X)>();
        var uld = addon->UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node == null || (ushort)node->Type < 1000 || !node->IsVisible()) continue;   // 1000+ = component nodes
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp == null || comp->GetComponentType() != ComponentType.DragDrop) continue;  // item slots are drag-and-drop boxes
            var scale = addon->Scale * (node->ScaleX > 0 ? node->ScaleX : 1f);
            var min = new Vector2(node->ScreenX, node->ScreenY);
            var max = min + new Vector2(node->Width * scale, node->Height * scale);
            list.Add((min, max, node->ScreenY, node->ScreenX));
        }
        // Rows first (rounded, so tiny differences don't split a row), then left to right.
        return list.OrderBy(s => MathF.Round(s.Y / 4f)).ThenBy(s => s.X).Select(s => (s.Min, s.Max)).ToList();
    }

    private static List<(Vector2 Min, Vector2 Max)> CoverRects()
    {
        var list = new List<(Vector2, Vector2)>();
        foreach (var name in Covers)
        {
            var addon = Addon(name);
            if (addon == null || !addon->IsVisible || addon->RootNode == null) continue;
            var root = addon->RootNode;
            var min = new Vector2(addon->X, addon->Y);
            var max = min + new Vector2(root->Width * addon->Scale, root->Height * addon->Scale);
            list.Add((min, max));
        }
        return list;
    }

    private static bool Overlaps((Vector2 Min, Vector2 Max) a, Vector2 min, Vector2 max) =>
        a.Min.X < max.X && a.Max.X > min.X && a.Min.Y < max.Y && a.Max.Y > min.Y;

    private static AtkUnitBase* Addon(string name)
    {
        var ptr = Plugin.GameGui.GetAddonByName(name);
        return ptr.IsNull ? null : (AtkUnitBase*)ptr.Address;
    }
}
