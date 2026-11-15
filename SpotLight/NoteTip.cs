using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace SpotLight;

/// <summary>
/// When you mouse over an important item that has a note, shows the note in a clear box
/// just above the game's item tooltip.
/// </summary>
public static unsafe class NoteTip
{
    public static void Draw()
    {
        try
        {
            var cfg = Plugin.Config;
            if (!cfg.ShowNotes) return;
            var hovered = (uint)(Plugin.GameGui.HoveredItem % 2_000_000);
            if (hovered == 0 || !cfg.IsImportant(hovered)) return;
            var note = cfg.NoteFor(hovered);
            if (note.Length == 0) return;

            var text = "Important: " + note;
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize() * cfg.NoteSize;
            var textSize = ImGui.CalcTextSize(text) * cfg.NoteSize;
            var pad = new Vector2(10f, 7f);
            var boxSize = textSize + pad * 2;

            // Only while the game's item tooltip is showing (so it never lingers), lined up just above it
            // (or just below it if there's no room above).
            var tip = Plugin.GameGui.GetAddonByName("ItemDetail");
            if (tip.IsNull) return;
            var addon = (AtkUnitBase*)tip.Address;
            if (!addon->IsVisible) return;
            var pos = new Vector2(addon->X, addon->Y - boxSize.Y - 4f);
            if (pos.Y < 0) pos.Y = addon->Y + (addon->RootNode != null ? addon->RootNode->Height * addon->Scale : 0f) + 4f;
            var screen = ImGui.GetMainViewport().Size;
            pos.X = Math.Clamp(pos.X, 0f, Math.Max(0f, screen.X - boxSize.X));
            pos.Y = Math.Clamp(pos.Y, 0f, Math.Max(0f, screen.Y - boxSize.Y));

            var draw = ImGui.GetForegroundDrawList();
            var border = ImGui.ColorConvertFloat4ToU32(cfg.Color);
            draw.AddRectFilled(pos, pos + boxSize, ImGui.ColorConvertFloat4ToU32(new Vector4(0.05f, 0.05f, 0.08f, 0.96f)), 6f);
            draw.AddRect(pos, pos + boxSize, border, 6f, ImDrawFlags.None, 3f);
            draw.AddText(font, size, pos + pad, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), text);
        }
        catch (Exception ex) { Plugin.Log.Verbose(ex, "Spot Light note skipped this frame."); }
    }
}
