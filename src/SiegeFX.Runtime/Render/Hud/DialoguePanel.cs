using System.Numerics;
using SiegeFX.Core.Assets;

namespace SiegeFX.Runtime.Render.Hud;

/// <summary>
/// Conversation overlay rendered to DS1's authored chrome
/// (<c>/ui/interfaces/backend/dialogue_box/dialogue_box.gas</c>): a top-centre
/// cpbox nine-slice frame with a recessed cpbox text panel, left-justified
/// copperplate speech, a corner X, and context buttons. Geometry is the
/// authored 640×480 reference scaled by viewport height.
///
/// We page a <see cref="ConversationDef"/> one node at a time (DS1 scrolls the
/// whole speech; paging keeps each voice line paired with its own screen).
/// Buttons follow the authored groups:
/// <list type="bullet">
///   <item><c>quest_dialog</c> OR <c>choice = potential_member</c> →
///     "Accept" / "Decline" (group <c>potential_member</c>). Accept emits the
///     node's <c>activate_quest</c> and, for a recruit offer, flags
///     <see cref="PendingRecruit"/> so the host adds the speaker to the party.
///     Decline advances to the polite-decline tail.</item>
///   <item>Anything else → a single bottom-right button, "Continue" mid-thread
///     and "Close" on the last line.</item>
/// </list>
/// The corner X dismisses at any time (a fork closed this way is "no answer").
/// If the speech overflows the panel we clip; shipped lines fit.
/// </summary>
public sealed class DialoguePanel
{
    public bool IsOpen { get; private set; }
    public string? PendingQuestActivation { get; private set; }

    /// <summary>Phase 26 — set true when the player hits Accept on a recruit
    /// offer node (<c>choice = potential_member</c>). The host consumes it via
    /// <see cref="ConsumePendingRecruit"/> to add the speaker to the party.</summary>
    public bool PendingRecruit { get; private set; }

    /// <summary>SC-PACKMULE — set on Accept of a <c>choice = buy_packmule</c>
    /// node; the host consumes it to charge the price and stable the mule.</summary>
    public bool PendingPackmule { get; private set; }

    public bool ConsumePendingPackmule()
    {
        if (!PendingPackmule) return false;
        PendingPackmule = false;
        return true;
    }

    // Authentic DS1 conversation chrome — /ui/interfaces/backend/
    // dialogue_box/dialogue_box.gas, authored in a 640×480 reference and
    // top-centre anchored. A cpbox nine-slice frame wraps a recessed cpbox
    // text panel; the copperplate text is left-justified; a recruit/quest
    // fork shows Accept/Decline at fixed rects, other lines show a single
    // Close button bottom-right, and the corner X always dismisses. Rects
    // are (x0,y0,x1,y1) in the 640×480 reference.
    static readonly (int x0, int y0, int x1, int y1) RFrame    = (187,   5, 602, 190);
    static readonly (int x0, int y0, int x1, int y1) RTextBg   = (201,  12, 564, 137);
    static readonly (int x0, int y0, int x1, int y1) RText     = (204,  15, 545, 134);
    static readonly (int x0, int y0, int x1, int y1) RAccept   = (205, 145, 297, 161);
    static readonly (int x0, int y0, int x1, int y1) RDecline  = (308, 145, 400, 161);
    static readonly (int x0, int y0, int x1, int y1) RCloseBtn = (471, 165, 563, 181);
    // button_shop (group shop): shown on choice = shop steps; opens the trade.
    static readonly (int x0, int y0, int x1, int y1) RShop     = (364, 165, 461, 181);
    // button_more / button_1 (generic_button_1) share the left slot.
    static readonly (int x0, int y0, int x1, int y1) RLeftBtn  = (205, 145, 297, 161);
    static readonly (int x0, int y0, int x1, int y1) RCloseX   = (581,  10, 597,  26);

    private readonly MenuButton _accept   = new("Accept",   0, 0, 10, 10);
    private readonly MenuButton _decline  = new("Decline",  0, 0, 10, 10);
    private readonly MenuButton _more     = new("More...",  0, 0, 10, 10);
    private readonly MenuButton _continue = new("Continue", 0, 0, 10, 10);
    private readonly MenuButton _shop     = new("Shop",     0, 0, 10, 10);
    private readonly MenuButton _button1  = new("",         0, 0, 10, 10);

    /// <summary>The visit ended with the Shop button (choice = shop); the host
    /// opens the speaker's trade.</summary>
    public bool PendingShop { get; private set; }

    public bool ConsumePendingShop()
    {
        var v = PendingShop;
        PendingShop = false;
        return v;
    }

    // Which buttons a step shows (dialogue_box.gas): Accept/Decline on forks;
    // More... or the authored button_1 in the left slot; Shop on choice = shop;
    // Close on every non-fork step.
    private static bool ShowsMore(DialogueNode n) => !n.IsChoiceFork && n.Choice == "more";
    private static bool ShowsButton1(DialogueNode n) =>
        !n.IsChoiceFork && n.Choice != "more" && n.ButtonText.Length > 0;
    private static bool ShowsShop(DialogueNode n) => n.Choice == "shop";
    private readonly MenuButton _closeX   = new("X",        0, 0, 10, 10);
    // ALPHA-2H — retail's text panel carries a right-edge scrollbar (the
    // authored RText column stops at 545 inside a 564-wide RTextBg for
    // exactly this reason). Arrows page one line; the thumb tracks.
    private readonly MenuButton _scrollUp   = new("-", 0, 0, 10, 10);
    private readonly MenuButton _scrollDown = new("+", 0, 0, 10, 10);
    private int _scroll;
    private int _visibleLines = 1;
    private int _totalLines = 1;

    static float Scale(int viewportH) => HudScale.Hud(viewportH);
    static (int x, int y, int w, int h) Px((int x0, int y0, int x1, int y1) r, float s, int originX)
        => (originX + (int)MathF.Round(r.x0 * s), (int)MathF.Round(r.y0 * s),
            (int)MathF.Round((r.x1 - r.x0) * s), (int)MathF.Round((r.y1 - r.y0) * s));
    static void Place(MenuButton b, (int x, int y, int w, int h) p)
    { b.X = p.x; b.Y = p.y; b.Width = p.w; b.Height = p.h; }

    private ConversationDef? _conv;
    private int _index;

    /// <summary>SC-QUEST-UI-D — the conversation currently on screen, so the
    /// journal can log its spoken text onto a quest at the acceptance edge.</summary>
    public ConversationDef? CurrentConversation => _conv;

    /// <summary>SC-QUEST-UI-D — the conversation that was on screen when the
    /// player last hit Accept. Accepting closes the panel (nulling
    /// <see cref="_conv"/>) in the same call the caller then reads, so the
    /// live reference is already gone; this snapshot survives the Close() so
    /// the journal can still log what the player heard.</summary>
    public ConversationDef? LastQuestConversation { get; private set; }

    /// <summary>Present a fresh conversation. Resets the cursor and clears any
    /// stale press state on the buttons. <paramref name="speakerName"/> is
    /// accepted for call-site compatibility but not drawn — DS1's
    /// dialogue_box.gas has no speaker-name control (the portrait/voice
    /// identifies the speaker).</summary>
    public void Open(string speakerName, ConversationDef conv, int numberedStepsPlayed = 0)
    {
        _ = speakerName;
        _conv = conv;
        // DS1 conversations are per visit: numbered steps play one visit at a
        // time, then the unnumbered repeat line (see ConversationDef).
        _numberedPlayed = Math.Max(0, numberedStepsPlayed);
        _index = conv.StartIndexForVisit(_numberedPlayed);
        PendingButtonValue = null;
        PendingShop = false;
        _scroll = 0;
        PendingQuestActivation = null;
        PendingQuestCompletion = null;
        PendingQuestDeactivation = null;
        PendingRecruit = false;
        LastQuestConversation = null;
        IsOpen = conv.Nodes.Count > 0;
        CancelAllPresses();
        NoteNodeShown();
    }

    // SC-QUEST-TURNIN — authored complete_quest*/deactivate_quest* fire when
    // their text node PLAYS (the turn-in monologue is the completion moment,
    // no Accept required). One-shot pendings; the host drains them per frame.
    public string? PendingQuestCompletion { get; private set; }
    public string? PendingQuestDeactivation { get; private set; }

    public string? ConsumePendingQuestCompletion()
    {
        var q = PendingQuestCompletion;
        PendingQuestCompletion = null;
        return q;
    }

    public string? ConsumePendingQuestDeactivation()
    {
        var q = PendingQuestDeactivation;
        PendingQuestDeactivation = null;
        return q;
    }

    // Numbered steps this NPC has played across visits (shown, or for an
    // Accept/Decline fork, accepted). A declined offer stays unplayed so the
    // next visit offers it again.
    private int _numberedPlayed;

    /// <summary>Authored <c>button_1_value</c> of the button that ended the visit
    /// (Utraea's "Directions": <c>d_0x&lt;scid&gt;</c>); the host consumes it.</summary>
    public string? PendingButtonValue { get; private set; }

    /// <summary>Conversation key and numbered-steps-played of the visit that just
    /// closed; the host stores it per NPC so the next visit continues from there.</summary>
    public (string Key, int NumberedStepsPlayed)? LastVisit { get; private set; }

    public (string Key, int NumberedStepsPlayed)? ConsumeLastVisit()
    {
        var v = LastVisit;
        LastVisit = null;
        return v;
    }

    public string? ConsumePendingButtonValue()
    {
        var v = PendingButtonValue;
        PendingButtonValue = null;
        return v;
    }

    private void MarkPlayed(DialogueNode n)
    {
        if (n.Order >= 0) _numberedPlayed = Math.Max(_numberedPlayed, _index + 1);
    }

    private void NoteNodeShown()
    {
        var n = CurrentNode;
        if (n is null) return;
        if (!n.IsChoiceFork) MarkPlayed(n);
        if (!string.IsNullOrWhiteSpace(n.CompleteQuest))   PendingQuestCompletion   = n.CompleteQuest;
        if (!string.IsNullOrWhiteSpace(n.DeactivateQuest)) PendingQuestDeactivation = n.DeactivateQuest;
    }

    public void Close()
    {
        if (_conv is not null) LastVisit = (_conv.Key, _numberedPlayed);
        IsOpen = false;
        _conv = null;
        CancelAllPresses();
    }

    private void CancelAllPresses()
    {
        _accept.CancelPress();
        _decline.CancelPress();
        _more.CancelPress();
        _continue.CancelPress();
        _closeX.CancelPress();
    }

    private DialogueNode? CurrentNode =>
        _conv is null || _index < 0 || _index >= _conv.Nodes.Count
            ? null : _conv.Nodes[_index];

    private void Layout(int viewportW, int viewportH)
    {
        // Map the authored 640×480 button rects to screen pixels, top-centre
        // anchored, so hit-testing matches what the player sees.
        float s = Scale(viewportH);
        int originX = (viewportW - (int)MathF.Round(640f * s)) / 2;

        Place(_accept,  Px(RAccept,   s, originX));
        Place(_decline, Px(RDecline,  s, originX));
        Place(_more,     Px(RLeftBtn, s, originX));
        Place(_button1,  Px(RLeftBtn, s, originX));
        Place(_shop,     Px(RShop, s, originX));
        Place(_continue, Px(RCloseBtn, s, originX));
        Place(_closeX,   Px(RCloseX, s, originX));

        // Retail wording: "More..." in the left slot, "Close" bottom-right
        // (reference screenshot, conversation.bmp); authored button_1_text
        // labels its own left-slot button (Utraea's "Directions"; Ehb's ella,
        // ordus, tarish, torg, overseer).
        var curNode = (_conv is not null && _index >= 0 && _index < _conv.Nodes.Count)
            ? _conv.Nodes[_index] : null;
        // Close ends the visit, except on a narrator banner with lines still
        // to come (only choice = more chains steps otherwise).
        bool last = _conv is null || curNode is null || !curNode.IsNonInteractive
                    || _index >= _conv.Nodes.Count - 1;
        _continue.Label = last ? "Close" : "More...";
        _more.Label = "More...";
        _button1.Label = curNode?.ButtonText ?? "";

        // ALPHA-2H — scrollbar arrows hug the text panel's right column.
        var bg = Px(RTextBg, s, originX);
        int sbW = Math.Max(10, (int)MathF.Round(14 * s));
        Place(_scrollUp,   (bg.x + bg.w - sbW, bg.y, sbW, sbW));
        Place(_scrollDown, (bg.x + bg.w - sbW, bg.y + bg.h - sbW, sbW, sbW));
    }

    public void OnMouseMove(int px, int py)
    {
        if (!IsOpen) return;
        _closeX.UpdateHover(px, py);
        var node = CurrentNode;
        if (node is null) return;
        if (node.IsChoiceFork)
        {
            _accept.UpdateHover(px, py);
            _decline.UpdateHover(px, py);
        }
        _continue.UpdateHover(px, py); // dialog_close: shown on every step
        if (ShowsMore(node)) _more.UpdateHover(px, py);
        if (ShowsButton1(node)) _button1.UpdateHover(px, py);
        if (ShowsShop(node)) _shop.UpdateHover(px, py);
    }

    public bool OnMouseDown(int px, int py)
    {
        if (!IsOpen) return false;
        _closeX.TryPress(px, py);
        _scrollUp.TryPress(px, py);
        _scrollDown.TryPress(px, py);
        var node = CurrentNode;
        if (node is null) return true;
        if (node.IsChoiceFork)
        {
            _accept.TryPress(px, py);
            _decline.TryPress(px, py);
        }
        _continue.TryPress(px, py);
        if (ShowsMore(node)) _more.TryPress(px, py);
        if (ShowsButton1(node)) _button1.TryPress(px, py);
        if (ShowsShop(node)) _shop.TryPress(px, py);
        return true;
    }

    public bool OnMouseUp(int px, int py)
    {
        if (!IsOpen) return false;
        // Corner X — always dismisses the conversation (a fork closed this
        // way counts as no answer: no recruit, no quest activation).
        if (_closeX.Release(px, py)) { Close(); return true; }
        // ALPHA-2H — scrollbar arrows page one wrapped line.
        if (_scrollUp.Release(px, py)) { _scroll = Math.Max(0, _scroll - 1); return true; }
        if (_scrollDown.Release(px, py)) { _scroll = Math.Min(Math.Max(0, _totalLines - _visibleLines), _scroll + 1); return true; }
        var node = CurrentNode;

        if (node is { IsChoiceFork: true })
        {
            if (_accept.Release(px, py))
            {
                MarkPlayed(node);
                PendingQuestActivation = node.ActivateQuest;
                // Phase 26 — recruit offer accepted: flag it so the host adds
                // the speaker to the party. A join node can also carry
                // activate_quest (Gyorn's join fires quest_gyorn_seek_overseer),
                // so both hooks fire off the one Accept.
                if (node.IsRecruitOffer) PendingRecruit = true;
                // SC-PACKMULE — mule sale accepted: the host stables one in.
                if (node.IsPackmuleOffer) PendingPackmule = true;
                LastQuestConversation = _conv; // snapshot before Close() nulls it
                Close();
                return true;
            }
            if (_decline.Release(px, py))
            {
                AdvanceToDeclineTail();
                return true;
            }
        }
        if (node is not null && _continue.Release(px, py))
        {
            // Close ends the visit (the next numbered step waits for the next
            // visit). Narrator banners keep chaining through their lines.
            if (node.IsNonInteractive) Advance();
            else Close();
            return true;
        }
        if (node is null) return true;
        if (ShowsMore(node) && _more.Release(px, py)) { Advance(); return true; }
        if (ShowsButton1(node) && _button1.Release(px, py))
        {
            // dialogue_button_1: hand its authored value to the host
            // (Utraea's "Directions": d_0x<speaker scid>).
            if (node.ButtonValue.Length > 0) PendingButtonValue = node.ButtonValue;
            Close();
            return true;
        }
        if (ShowsShop(node) && _shop.Release(px, py))
        {
            PendingShop = true; // dialogue_shop
            Close();
            return true;
        }
        return true; // consume the up-event regardless so it doesn't fall through
    }

    private void Advance()
    {
        if (_conv is null) { Close(); return; }
        _index++;
        _scroll = 0; // fresh node starts at the top
        if (_index >= _conv.Nodes.Count) Close();
        else NoteNodeShown();
    }

    /// <summary>Decline path: jump to the first remaining node that *isn't* a
    /// quest-dialog node (the no-order tail in DS1's authoring convention).
    /// If none exists, close immediately.</summary>
    private void AdvanceToDeclineTail()
    {
        if (_conv is null) { Close(); return; }
        for (int i = _index + 1; i < _conv.Nodes.Count; i++)
        {
            if (!_conv.Nodes[i].IsChoiceFork) { _index = i; NoteNodeShown(); return; }
        }
        Close();
    }

    /// <summary>Caller pulls the activated quest key once after Accept fires.
    /// One-shot to keep the quest hook non-idempotent — the same conversation
    /// re-opened wouldn't auto-fire the quest a second time.</summary>
    public string? ConsumePendingQuestActivation()
    {
        var q = PendingQuestActivation;
        PendingQuestActivation = null;
        return q;
    }

    /// <summary>Phase 26 — one-shot read of the recruit flag. The host calls
    /// this once after a dialogue closes; a true return means the player
    /// accepted the join offer and the speaker should enter the party.</summary>
    public bool ConsumePendingRecruit()
    {
        var r = PendingRecruit;
        PendingRecruit = false;
        return r;
    }

    public void Draw(BarRenderer bars, TextRenderer text, IconRenderer? icons,
                     Func<string, GlTexture?>? guiTex, int viewportW, int viewportH)
    {
        if (!IsOpen) return;
        var node = CurrentNode;
        if (node is null) { Close(); return; }
        Layout(viewportW, viewportH);

        float s = Scale(viewportH);
        int originX = (viewportW - (int)MathF.Round(640f * s)) / 2;
        var ink = new Vector4(0.90f, 0.86f, 0.74f, 1f);

        // ALPHA-2H — retail chrome (reference: conversation.bmp): a near-black
        // slightly-translucent panel with a thin two-tone gold border, NOT the
        // parchment cpbox (the earlier cpbox read as scaffolding against the
        // reference). Same authored geometry, restyled fills.
        void DarkPanel((int x0, int y0, int x1, int y1) r, bool recessed)
        {
            var p = Px(r, s, originX);
            bars.DrawRect(viewportW, viewportH, p.x, p.y, p.w, p.h,
                recessed ? new Vector4(0.015f, 0.015f, 0.02f, 0.92f)
                         : new Vector4(0.05f, 0.05f, 0.06f, 0.94f));
            bars.DrawBorder(viewportW, viewportH, p.x, p.y, p.w, p.h,
                recessed ? new Vector4(0.35f, 0.31f, 0.20f, 1f)
                         : new Vector4(0.62f, 0.55f, 0.36f, 1f));
        }

        DarkPanel(RFrame, recessed: false);
        DarkPanel(RTextBg, recessed: true);

        // Left-justified, word-wrapped speech with a working scroll window.
        var tr = Px(RText, s, originX);
        int lineH = (text.HasFont ? text.Font!.Height : 14) + 2;
        _visibleLines = Math.Max(1, tr.h / lineH);
        var wrapped = new List<string>();
        foreach (var rawLine in node.Text.Replace("\\n", "\n").Split('\n'))
            wrapped.AddRange(WrapLine(rawLine, text, tr.w));
        _totalLines = Math.Max(1, wrapped.Count);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _totalLines - _visibleLines));
        for (int line = 0; line < _visibleLines && _scroll + line < wrapped.Count; line++)
            text.DrawString(viewportW, viewportH, wrapped[_scroll + line], tr.x, tr.y + line * lineH, ink);

        // ALPHA-2H — right-edge scrollbar (arrows + track + proportional
        // thumb), matching retail's text panel column.
        {
            var bg = Px(RTextBg, s, originX);
            int sbW = Math.Max(10, (int)MathF.Round(14 * s));
            int trackX = bg.x + bg.w - sbW;
            int trackY = bg.y + sbW;
            int trackH = Math.Max(1, bg.h - 2 * sbW);
            bars.DrawRect(viewportW, viewportH, trackX, trackY, sbW, trackH, new Vector4(0.09f, 0.09f, 0.10f, 1f));
            bars.DrawBorder(viewportW, viewportH, trackX, trackY, sbW, trackH, new Vector4(0.35f, 0.31f, 0.20f, 1f));
            int span = Math.Max(1, _totalLines);
            int thumbH = Math.Max(8, trackH * _visibleLines / span);
            int scrollMax = Math.Max(1, _totalLines - _visibleLines);
            int thumbY = trackY + (trackH - thumbH) * Math.Min(_scroll, scrollMax) / scrollMax;
            if (_totalLines <= _visibleLines) { thumbH = trackH; thumbY = trackY; }
            bars.DrawRect(viewportW, viewportH, trackX + 1, thumbY + 1, sbW - 2, Math.Max(1, thumbH - 2),
                new Vector4(0.42f, 0.37f, 0.24f, 1f));
            _scrollUp.Draw(bars, text, icons, guiTex, viewportW, viewportH);
            _scrollDown.Draw(bars, text, icons, guiTex, viewportW, viewportH);
        }

        // Context buttons — Accept/Decline for a recruit or quest fork
        // (group = potential_member), a single Close/Continue otherwise.
        // Passing icons/guiTex routes each through the authored button_4
        // 3-slice chrome (every DS1 in-panel button shares it).
        if (node.IsChoiceFork)
        {
            _accept.Draw(bars, text, icons, guiTex, viewportW, viewportH);
            _decline.Draw(bars, text, icons, guiTex, viewportW, viewportH);
        }
        _continue.Draw(bars, text, icons, guiTex, viewportW, viewportH);
        if (ShowsMore(node)) _more.Draw(bars, text, icons, guiTex, viewportW, viewportH);
        if (ShowsButton1(node)) _button1.Draw(bars, text, icons, guiTex, viewportW, viewportH);
        if (ShowsShop(node)) _shop.Draw(bars, text, icons, guiTex, viewportW, viewportH);

        // Corner X — the authored red-X raw (b_gui_cmn_button_x_*) when it
        // resolves, MenuButton chrome otherwise.
        var xTex = guiTex?.Invoke(_closeX.Pressed ? "b_gui_cmn_button_x_dwn"
                     : _closeX.Hovered ? "b_gui_cmn_button_x_hov"
                                       : "b_gui_cmn_button_x_up")
                   ?? guiTex?.Invoke("b_gui_cmn_button_x_up");
        if (xTex is not null && icons is not null)
            icons.DrawIcon(viewportW, viewportH, xTex,
                _closeX.X, _closeX.Y, _closeX.Width, _closeX.Height, Vector4.One);
        else
            _closeX.Draw(bars, text, icons, guiTex, viewportW, viewportH);
    }

    /// <summary>Word-wrap to a pixel width using the active font. Falls back to
    /// the raw line when measurement isn't possible (no font yet) so the panel
    /// is still readable in the "font failed to load" diagnostic case.</summary>
    private static IEnumerable<string> WrapLine(string raw, TextRenderer text, int maxWidthPx)
    {
        if (string.IsNullOrEmpty(raw)) { yield return ""; yield break; }
        if (!text.HasFont) { yield return raw; yield break; }

        var words = raw.Split(' ');
        var line  = "";
        foreach (var w in words)
        {
            var probe = line.Length == 0 ? w : line + " " + w;
            if (text.MeasureWidth(probe) <= maxWidthPx) { line = probe; continue; }
            if (line.Length > 0) yield return line;
            line = w;
        }
        if (line.Length > 0) yield return line;
    }
}
