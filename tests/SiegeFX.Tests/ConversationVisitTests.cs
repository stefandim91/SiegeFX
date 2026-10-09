using SiegeFX.Core.Assets;

namespace SiegeFX.Tests;

public class ConversationVisitTests
{
    // Shaped like Elddim's Zabar: quest on visit 1, advice on visit 2,
    // then the unnumbered "Back again?" line on every later visit.
    private static ConversationDef Zabar() => ConversationStore.LoadFromDocument(GasDocument.Parse("""
        [conversation_zabar]
        {
            [text*]
            {
                screen_text = "Back again?";
                button_1_text = "Directions";
                button_1_value = d_0x03200c26;
            }
            [text*] { order = 1; screen_text = "Talk to the Cap'n."; }
            [text*] { order = 0; screen_text = "Take this quest."; quest_dialog = true; }
        }
        """))["conversation_zabar"];

    [Fact]
    public void Numbered_steps_lead_in_order_and_the_repeat_line_follows()
    {
        var conv = Zabar();
        Assert.Equal(new[] { 0, 1, -1 }, conv.Nodes.Select(n => n.Order));
        Assert.Equal(2, conv.OrderedCount);
    }

    [Theory]
    [InlineData(0, 0)] // first visit: the quest
    [InlineData(1, 1)] // second visit: the advice
    [InlineData(2, 2)] // later visits: the repeat line
    [InlineData(9, 2)]
    public void Each_visit_starts_at_the_next_unplayed_step(int played, int expectedIndex)
    {
        Assert.Equal(expectedIndex, Zabar().StartIndexForVisit(played));
    }

    [Fact]
    public void Without_a_repeat_line_the_last_step_repeats()
    {
        var conv = ConversationStore.LoadFromDocument(GasDocument.Parse("""
            [c]
            {
                [text*] { order = 0; screen_text = "One."; }
                [text*] { order = 1; screen_text = "Two."; }
            }
            """))["c"];
        Assert.Equal(1, conv.StartIndexForVisit(5));
    }

    [Fact]
    public void The_directions_button_value_is_read()
    {
        var repeat = Zabar().Nodes[2];
        Assert.Equal("Directions", repeat.ButtonText);
        Assert.Equal("d_0x03200c26", repeat.ButtonValue);
    }
}
