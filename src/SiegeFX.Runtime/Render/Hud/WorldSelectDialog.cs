using System;
using System.Numerics;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Frontend modal shown before character creation. It chooses the authored
/// world while leaving networking policy to the host.
/// </summary>
public sealed class WorldSelectDialog
{
    public enum Result { None, KingdomOfEhb, UtraeanPeninsula, Cancel }

    public bool IsOpen { get; private set; }

    private const int RefW = 640;
    private const int RefH = 480;

    // Authored in the common 640x480 modal coordinate space.
    private static readonly (int x0, int y0, int x1, int y1) RPanel = (164, 92, 476, 390);
    private static readonly (int x0, int y0, int x1, int y1) REhb = (190, 164, 450, 224);
    private static readonly (int x0, int y0, int x1, int y1) RUtraea = (190, 244, 450, 304);
    private static readonly (int x0, int y0, int x1, int y1) RBack = (270, 336, 370, 360);

    private (int x, int y, int w, int h) _panel;
    private (int x, int y, int w, int h) _ehb;
    private (int x, int y, int w, int h) _utraea;
    private (int x, int y, int w, int h) _back;

    private enum Button { None, KingdomOfEhb, UtraeanPeninsula, Back }

    private Button _pressed;
    private Button _hover;

    public void Open()
    {
        _pressed = Button.None;
        _hover = Button.None;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _pressed = Button.None;
        _hover = Button.None;
    }

    public void OnMouseMove(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        _hover = HitButton(px, py);
    }

    /// <summary>Latches the button under the pointer. The dialog is modal, so
    /// the host should consume the click whenever <see cref="IsOpen"/> is true.</summary>
    public void OnMouseDown(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);
        _pressed = HitButton(px, py);
    }

    /// <summary>Activates only when the pointer is released over the same
    /// button on which the press began.</summary>
    public Result OnMouseUp(int px, int py, int vw, int vh)
    {
        if (!IsOpen) return Result.None;
        Layout(vw, vh);

        Button released = HitButton(px, py);
        Button pressed = _pressed;
        _pressed = Button.None;

        if (released == Button.None || released != pressed)
            return Result.None;

        return released switch
        {
            Button.KingdomOfEhb => Result.KingdomOfEhb,
            Button.UtraeanPeninsula => Result.UtraeanPeninsula,
            Button.Back => Result.Cancel,
            _ => Result.None,
        };
    }

    public void Draw(BarRenderer bars, TextRenderer text, int vw, int vh)
    {
        if (!IsOpen) return;
        Layout(vw, vh);

        float scale = HudScale.Modal(vw, vh);
        int fontScale = Math.Max(1, (int)MathF.Round(scale));
        int titleScale = Math.Max(1, (int)MathF.Round(scale * 1.16f));

        var panelFill = new Vector4(0.035f, 0.03f, 0.025f, 0.94f);
        var panelEdge = new Vector4(0.52f, 0.43f, 0.27f, 1f);
        var gold = new Vector4(1f, 0.90f, 0.55f, 1f);
        var parchment = new Vector4(0.88f, 0.82f, 0.70f, 1f);
        var dim = new Vector4(0.62f, 0.58f, 0.50f, 1f);

        bars.DrawRect(vw, vh, _panel.x, _panel.y, _panel.w, _panel.h, panelFill);
        bars.DrawBorder(vw, vh, _panel.x, _panel.y, _panel.w, _panel.h, panelEdge);

        const string heading = "Choose your adventure";
        int headingWidth = text.MeasureWidth(heading, titleScale);
        int headingY = _panel.y + (int)MathF.Round(25 * scale);
        text.DrawString(vw, vh, heading,
            _panel.x + (_panel.w - headingWidth) / 2,
            headingY, gold, titleScale);

        DrawAdventureButton(bars, text, vw, vh, _ehb,
            "Kingdom of Ehb", "Original Campaign", Button.KingdomOfEhb,
            fontScale, parchment, dim);
        DrawAdventureButton(bars, text, vw, vh, _utraea,
            "Utraean Peninsula", "Solo Adventure", Button.UtraeanPeninsula,
            fontScale, parchment, dim);
        DrawBackButton(bars, text, vw, vh, fontScale, parchment);
    }

    private void DrawAdventureButton(
        BarRenderer bars, TextRenderer text, int vw, int vh,
        (int x, int y, int w, int h) rect,
        string title, string subtitle, Button id, int fontScale,
        Vector4 ink, Vector4 subtitleInk)
    {
        DrawButtonFrame(bars, vw, vh, rect, id);

        int lineHeight = Math.Max(1, text.LineHeight * fontScale);
        int gap = Math.Max(1, fontScale * 3);
        int blockHeight = lineHeight * 2 + gap;
        int y = rect.y + (rect.h - blockHeight) / 2;
        if (_pressed == id) y += fontScale;

        Vector4 titleInk = _hover == id
            ? new Vector4(1f, 0.96f, 0.85f, 1f)
            : ink;
        int titleWidth = text.MeasureWidth(title, fontScale);
        int subtitleWidth = text.MeasureWidth(subtitle, fontScale);
        text.DrawString(vw, vh, title, rect.x + (rect.w - titleWidth) / 2,
            y, titleInk, fontScale);
        text.DrawString(vw, vh, subtitle, rect.x + (rect.w - subtitleWidth) / 2,
            y + lineHeight + gap, subtitleInk, fontScale);
    }

    private void DrawBackButton(
        BarRenderer bars, TextRenderer text, int vw, int vh,
        int fontScale, Vector4 ink)
    {
        DrawButtonFrame(bars, vw, vh, _back, Button.Back);

        const string label = "Back";
        int width = text.MeasureWidth(label, fontScale);
        int height = text.LineHeight * fontScale;
        int yOffset = _pressed == Button.Back ? fontScale : 0;
        Vector4 labelInk = _hover == Button.Back
            ? new Vector4(1f, 0.96f, 0.85f, 1f)
            : ink;
        text.DrawString(vw, vh, label,
            _back.x + (_back.w - width) / 2,
            _back.y + (_back.h - height) / 2 + yOffset,
            labelInk, fontScale);
    }

    private void DrawButtonFrame(
        BarRenderer bars, int vw, int vh,
        (int x, int y, int w, int h) rect, Button id)
    {
        Vector4 fill = _pressed == id
            ? new Vector4(0.22f, 0.15f, 0.07f, 0.98f)
            : _hover == id
                ? new Vector4(0.25f, 0.18f, 0.09f, 0.96f)
                : new Vector4(0.12f, 0.10f, 0.07f, 0.94f);
        Vector4 edge = _hover == id || _pressed == id
            ? new Vector4(0.78f, 0.63f, 0.32f, 1f)
            : new Vector4(0.42f, 0.36f, 0.26f, 1f);

        bars.DrawRect(vw, vh, rect.x, rect.y, rect.w, rect.h, fill);
        bars.DrawBorder(vw, vh, rect.x, rect.y, rect.w, rect.h, edge);
    }

    private void Layout(int vw, int vh)
    {
        float scale = HudScale.Modal(vw, vh);
        int ox = (int)MathF.Round((vw - RefW * scale) / 2f);
        int oy = (int)MathF.Round((vh - RefH * scale) / 2f);

        (int x, int y, int w, int h) Screen((int x0, int y0, int x1, int y1) rect)
            => (
                ox + (int)MathF.Round(rect.x0 * scale),
                oy + (int)MathF.Round(rect.y0 * scale),
                Math.Max(1, (int)MathF.Round((rect.x1 - rect.x0) * scale)),
                Math.Max(1, (int)MathF.Round((rect.y1 - rect.y0) * scale)));

        _panel = Screen(RPanel);
        _ehb = Screen(REhb);
        _utraea = Screen(RUtraea);
        _back = Screen(RBack);
    }

    private Button HitButton(int px, int py)
    {
        if (Contains(_ehb, px, py)) return Button.KingdomOfEhb;
        if (Contains(_utraea, px, py)) return Button.UtraeanPeninsula;
        if (Contains(_back, px, py)) return Button.Back;
        return Button.None;
    }

    private static bool Contains((int x, int y, int w, int h) rect, int px, int py)
        => px >= rect.x && px < rect.x + rect.w
            && py >= rect.y && py < rect.y + rect.h;
}
