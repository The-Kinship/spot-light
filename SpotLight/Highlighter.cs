using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Inventory;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpotLight;

/// <summary>
/// Draws the highlights on top of the game's storage windows. Read only: it looks at where the slots are on screen
/// and which item sits in each one, then draws boxes over them. It never touches the items themselves.
///
/// Windows with tabs (saddlebag, FC chest, retainers, the smaller inventory layouts) don't say which page is showing,
/// so the icons on screen are compared with each page's contents and the matching page is used.
/// If no page clearly matches, nothing is drawn for that grid (better nothing than the wrong slot).
/// </summary>
public static unsafe class Highlighter
{
    private readonly record struct Slot(Vector2 Min, Vector2 Max, uint Icon);

    private static readonly GameInventoryType[] Bags =
        { GameInventoryType.Inventory1, GameInventoryType.Inventory2, GameInventoryType.Inventory3, GameInventoryType.Inventory4 };
    private static readonly GameInventoryType[] Saddlebags =
        { GameInventoryType.SaddleBag1, GameInventoryType.SaddleBag2, GameInventoryType.PremiumSaddleBag1, GameInventoryType.PremiumSaddleBag2 };
    private static readonly GameInventoryType[] FcPages =
    {
        GameInventoryType.FreeCompanyPage1, GameInventoryType.FreeCompanyPage2, GameInventoryType.FreeCompanyPage3,
        GameInventoryType.FreeCompanyPage4, GameInventoryType.FreeCompanyPage5,
    };
    private static readonly GameInventoryType[] RetainerPages =
    {
        GameInventoryType.RetainerPage1, GameInventoryType.RetainerPage2, GameInventoryType.RetainerPage3, GameInventoryType.RetainerPage4,
        GameInventoryType.RetainerPage5, GameInventoryType.RetainerPage6, GameInventoryType.RetainerPage7,
    };

    /// <summary>Game windows that can pop up over storage; highlights under them are skipped so they stay readable.</summary>
    private static readonly string[] Covers = { "ItemDetail", "ContextMenu", "InventoryContextMenu", "ContextIconMenu" };

    /// <summary>How many important items were highlighted last frame (shown in the settings).</summary>
    public static int ShownLastFrame { get; private set; }

    public static void Draw()
    {
        ShownLastFrame = 0;
        var cfg = Plugin.Config;
        if (!cfg.HighlightOn || (cfg.Important.Count == 0 && !cfg.DimOthers)) return;
        try
        {
            var ctx = new Ctx(ImGui.GetBackgroundDrawList(), CoverRects());

            // Inventory, "all bags open": one grid per bag, always in this order.
            for (var b = 0; b < 4; b++)
                FixedGrid(ctx, $"InventoryGrid{b}E", Items(Bags[b]));

            // Inventory, normal and large layouts: tabbed, so match the page.
            MatchedGrids(ctx, "InventoryGrid", BagViews());
            MatchedGrids(ctx, "InventoryGrid0", BagViews());
            MatchedGrids(ctx, "InventoryGrid1", BagViews());

            // Saddlebag (two bags side by side; the premium saddlebag is another tab).
            MatchedGrids(ctx, "InventoryBuddy", Saddlebags.Select(Items).ToList());
            MatchedGrids(ctx, "InventoryBuddy2", Saddlebags.Select(Items).ToList());

            // Retainer: its 175 slots are shown 35 at a time (one grid in the small window, five in the large one).
            var retainer = RetainerViews();
            MatchedGrids(ctx, "RetainerGrid", retainer);
            for (var g = 0; g < 5; g++) MatchedGrids(ctx, $"RetainerGrid{g}", retainer);

            // Free Company chest (one page at a time).
            MatchedGrids(ctx, "FreeCompanyChest", FcPages.Select(Items).ToList());
        }
        catch (Exception ex)
        {
            // Never let a drawing problem spam or break anything; just stop for this frame.
            Plugin.Log.Verbose(ex, "Spot Light drawing skipped this frame.");
        }
    }

    private sealed record Ctx(ImDrawListPtr Draw, List<(Vector2 Min, Vector2 Max)> Covers);

    // ---------- What each container holds ----------

    private static uint[] Items(GameInventoryType type)
    {
        var span = Plugin.GameInventory.GetInventoryItems(type);
        var ids = new uint[span.Length];
        for (var i = 0; i < span.Length; i++) ids[i] = span[i].ItemId;
        return ids;
    }

    private static List<uint[]> BagViews() => Bags.Select(Items).ToList();

    /// <summary>The retainer's 7 pages of 25, laid end to end, then cut into the 5 screens of 35 the game shows.</summary>
    private static List<uint[]> RetainerViews()
    {
        var all = new List<uint>();
        foreach (var page in RetainerPages)
        {
            var items = Items(page);
            for (var i = 0; i < 25; i++) all.Add(i < items.Length ? items[i] : 0);
        }
        var views = new List<uint[]>();
        for (var c = 0; c < 5; c++) views.Add(all.Skip(c * 35).Take(35).ToArray());
        return views;
    }

    // ---------- Grids ----------

    /// <summary>A grid whose contents are known for sure (the "all bags open" inventory).</summary>
    private static void FixedGrid(Ctx ctx, string addonName, uint[] items)
    {
        var addon = Addon(addonName);
        if (addon == null || !addon->IsVisible) return;
        var grids = SplitAtGaps(SlotsOf(addon));
        if (grids.Count != 1 || grids[0].Count != items.Length) return;
        DrawSlots(ctx, grids[0], items);
    }

    /// <summary>A window with one or more grids whose page isn't known: each grid uses the page its icons match.</summary>
    private static void MatchedGrids(Ctx ctx, string addonName, List<uint[]> views)
    {
        var addon = Addon(addonName);
        if (addon == null || !addon->IsVisible) return;
        foreach (var (grid, view) in Matches(addon, views)) DrawSlots(ctx, grid, view);
    }

    /// <summary>
    /// Reads the window's slots a few different ways (split at gaps, as one block, or as 5-wide bags side by side)
    /// and returns the grids that clearly match a page, using the first way that matches anything.
    /// </summary>
    private static List<(List<Slot> Grid, uint[] View)> Matches(AtkUnitBase* addon, List<uint[]> views)
    {
        var slots = SlotsOf(addon);
        foreach (var layout in Layouts(slots))
        {
            var found = new List<(List<Slot>, uint[])>();
            foreach (var grid in layout)
            {
                var best = BestMatch(grid, views);
                if (best != null) found.Add((grid, best));
            }
            if (found.Count > 0) return found;
        }
        return new();
    }

    private static IEnumerable<List<List<Slot>>> Layouts(List<Slot> slots)
    {
        if (slots.Count == 0) yield break;
        yield return SplitAtGaps(slots);
        yield return new List<List<Slot>> { RowMajor(slots) };
        yield return SplitEvery5Columns(slots);
    }

    private static List<Slot> RowMajor(IEnumerable<Slot> slots) =>
        slots.OrderBy(s => MathF.Round(s.Min.Y / 4f)).ThenBy(s => s.Min.X).ToList();

    /// <summary>Bags drawn side by side, each 5 slots wide.</summary>
    private static List<List<Slot>> SplitEvery5Columns(List<Slot> slots)
    {
        var columns = slots.Select(s => MathF.Round(s.Min.X)).Distinct().OrderBy(x => x).ToList();
        return slots.GroupBy(s => columns.IndexOf(MathF.Round(s.Min.X)) / 5).OrderBy(g => g.Key).Select(g => RowMajor(g)).ToList();
    }

    /// <summary>For /spotlight debug: what Spot Light sees in each storage window that's open.</summary>
    public static List<string> Diagnose()
    {
        var lines = new List<string>();
        void Check(string name, List<uint[]> views)
        {
            var addon = Addon(name);
            if (addon == null || !addon->IsVisible) return;
            var slots = SlotsOf(addon);
            var nonEmpty = views.Select(v => v.Count(i => i != 0)).ToList();
            var gaps = SplitAtGaps(slots).Select(g => g.Count);
            var matched = Matches(addon, views).Count;
            lines.Add($"{name}: {slots.Count} slots, grids by gaps [{string.Join(",", gaps)}], pages {views.Count} " +
                      $"(sizes {string.Join(",", views.Select(v => v.Length))}, items {string.Join(",", nonEmpty)}), matched grids: {matched}");
        }
        for (var b = 0; b < 4; b++) Check($"InventoryGrid{b}E", new List<uint[]> { Items(Bags[b]) });
        Check("InventoryGrid", BagViews());
        Check("InventoryGrid0", BagViews());
        Check("InventoryGrid1", BagViews());
        Check("InventoryBuddy", Saddlebags.Select(Items).ToList());
        Check("InventoryBuddy2", Saddlebags.Select(Items).ToList());
        Check("RetainerGrid", RetainerViews());
        for (var g = 0; g < 5; g++) Check($"RetainerGrid{g}", RetainerViews());
        Check("FreeCompanyChest", FcPages.Select(Items).ToList());
        if (lines.Count == 0) lines.Add("No storage windows are open.");
        return lines;
    }

    /// <summary>The page whose items' icons line up with the icons on screen, or null if none clearly does.</summary>
    private static uint[]? BestMatch(List<Slot> grid, List<uint[]> views)
    {
        uint[]? best = null;
        int bestScore = 0, bestMiss = int.MaxValue;
        foreach (var view in views)
        {
            if (view.Length != grid.Count) continue;
            int score = 0, miss = 0;
            for (var i = 0; i < grid.Count; i++)
            {
                var shown = grid[i].Icon % 1_000_000;   // HQ icons are +1,000,000
                var item = view[i];
                if (item == 0 && shown == 0) continue;
                var expected = item == 0 ? 0u : (ItemNames.Get(item)?.Icon ?? 0u);
                if (item != 0 && shown == expected) score++;
                else miss++;
            }
            if (score > bestScore || (score == bestScore && miss < bestMiss)) { best = view; bestScore = score; bestMiss = miss; }
        }
        // Needs real agreement: at least one matching item, and mostly matches.
        return bestScore > 0 && bestMiss <= bestScore / 4 ? best : null;
    }

    // ---------- Drawing ----------

    private static void DrawSlots(Ctx ctx, List<Slot> slots, uint[] items)
    {
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
        var draw = ctx.Draw;

        for (var i = 0; i < slots.Count && i < items.Length; i++)
        {
            var id = items[i];
            if (id == 0) continue;
            var (min, max, _) = slots[i];
            if (ctx.Covers.Any(c => Overlaps(c, min, max))) continue;

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

    // ---------- Reading the game window ----------

    /// <summary>
    /// The item slots of a window, split into its grids (a big gap between columns starts a new grid),
    /// each in the bag's own order: left to right, top to bottom.
    /// </summary>
    private static List<Slot> SlotsOf(AtkUnitBase* addon)
    {
        var slots = new List<Slot>();
        var uld = addon->UldManager;
        for (var i = 0; i < uld.NodeListCount; i++)
        {
            var node = uld.NodeList[i];
            if (node == null || (ushort)node->Type < 1000 || !node->IsVisible()) continue;   // 1000+ = component nodes
            var comp = ((AtkComponentNode*)node)->Component;
            if (comp == null || comp->GetComponentType() != ComponentType.DragDrop) continue;  // item slots are drag-and-drop boxes
            var dd = (AtkComponentDragDrop*)comp;
            var icon = dd->AtkComponentIcon != null ? dd->AtkComponentIcon->IconId : 0u;
            var scale = addon->Scale * (node->ScaleX > 0 ? node->ScaleX : 1f);
            var min = new Vector2(node->ScreenX, node->ScreenY);
            slots.Add(new Slot(min, min + new Vector2(node->Width * scale, node->Height * scale), icon));
        }
        return slots;
    }

    private static List<List<Slot>> SplitAtGaps(List<Slot> slots)
    {
        if (slots.Count == 0) return new();

        // Columns (by X), then split into grids wherever the gap between columns is much bigger than usual.
        var columns = slots.Select(s => MathF.Round(s.Min.X)).Distinct().OrderBy(x => x).ToList();
        var width = slots[0].Max.X - slots[0].Min.X;
        var gridOfColumn = new Dictionary<float, int>();
        var grid = 0;
        for (var c = 0; c < columns.Count; c++)
        {
            if (c > 0 && columns[c] - columns[c - 1] > width * 1.6f) grid++;
            gridOfColumn[columns[c]] = grid;
        }

        return slots.GroupBy(s => gridOfColumn[MathF.Round(s.Min.X)])
            .OrderBy(g => g.Key)
            .Select(g => RowMajor(g))
            .ToList();
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
