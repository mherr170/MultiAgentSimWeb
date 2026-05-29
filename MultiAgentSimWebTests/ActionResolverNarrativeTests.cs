using MultiAgentSimWeb.Models;
using MultiAgentSimWeb.Services;

namespace MultiAgentSimWebTests;

public class ActionResolverNarrativeTests
{
    private const string Agent = "Alice";
    private const string Color = "#ff0000";

    // ── Thought ───────────────────────────────────────────────────────────────

    [Fact]
    public void NarrativeEvents_WithThought_EmitsThoughtEvent()
    {
        var action = new AgentAction { Thought = "I should find water." };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();

        var ev = Assert.Single(events, e => e.Type == "thought");
        Assert.Equal(Agent, ev.AgentName);
        Assert.Equal(Color, ev.AgentColor);
        Assert.Equal("thinks", ev.Label);
        Assert.Equal("I should find water.", ev.Content);
    }

    [Fact]
    public void NarrativeEvents_EmptyThought_NoThoughtEvent()
    {
        var action = new AgentAction { Thought = "" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "thought");
    }

    [Fact]
    public void NarrativeEvents_WhitespaceThought_NoThoughtEvent()
    {
        var action = new AgentAction { Thought = "   " };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "thought");
    }

    // ── Speech ────────────────────────────────────────────────────────────────

    [Fact]
    public void NarrativeEvents_WithSpeech_EmitsSpeechEvent()
    {
        var action = new AgentAction { Speech = "Is anyone there?" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();

        var ev = Assert.Single(events, e => e.Type == "speech");
        Assert.Equal(Agent, ev.AgentName);
        Assert.Equal("says", ev.Label);
        Assert.Contains("Is anyone there?", ev.Content);
    }

    [Fact]
    public void NarrativeEvents_SpeechContent_IsWrappedInQuotes()
    {
        var action = new AgentAction { Speech = "Hello!" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        var ev = events.Single(e => e.Type == "speech");
        Assert.Equal("\"Hello!\"", ev.Content);
    }

    [Fact]
    public void NarrativeEvents_EmptySpeech_NoSpeechEvent()
    {
        var action = new AgentAction { Speech = "" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "speech");
    }

    [Fact]
    public void NarrativeEvents_WhitespaceSpeech_NoSpeechEvent()
    {
        var action = new AgentAction { Speech = "\t\n" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "speech");
    }

    // ── Action ────────────────────────────────────────────────────────────────

    [Fact]
    public void NarrativeEvents_WithAction_EmitsActionEvent()
    {
        var action = new AgentAction { Action = "searches through the debris" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();

        var ev = Assert.Single(events, e => e.Type == "action");
        Assert.Equal("does", ev.Label);
        Assert.Equal("searches through the debris", ev.Content);
    }

    [Fact]
    public void NarrativeEvents_ActionIsNothing_NoActionEvent()
    {
        var action = new AgentAction { Action = "nothing" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "action");
    }

    [Fact]
    public void NarrativeEvents_EmptyAction_NoActionEvent()
    {
        var action = new AgentAction { Action = "" };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "action");
    }

    [Fact]
    public void NarrativeEvents_WhitespaceAction_NoActionEvent()
    {
        var action = new AgentAction { Action = "   " };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.DoesNotContain(events, e => e.Type == "action");
    }

    // ── Combinations ─────────────────────────────────────────────────────────

    [Fact]
    public void NarrativeEvents_AllThreeSet_EmitsThreeEventsInOrder()
    {
        var action = new AgentAction
        {
            Thought = "Stay alert.",
            Speech  = "Hey!",
            Action  = "waves frantically"
        };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();

        Assert.Equal(3, events.Count);
        Assert.Equal("thought", events[0].Type);
        Assert.Equal("speech",  events[1].Type);
        Assert.Equal("action",  events[2].Type);
    }

    [Fact]
    public void NarrativeEvents_NoneSet_EmitsNoEvents()
    {
        var action = new AgentAction
        {
            Thought = "",
            Speech  = "",
            Action  = ""
        };
        var events = ActionResolver.NarrativeEvents(Agent, Color, action).ToList();
        Assert.Empty(events);
    }

    [Fact]
    public void NarrativeEvents_AgentNameAndColorPropagatedToAllEvents()
    {
        const string name  = "Marcus";
        const string color = "#00ff99";
        var action = new AgentAction
        {
            Thought = "hmm",
            Speech  = "yo",
            Action  = "nods"
        };
        var events = ActionResolver.NarrativeEvents(name, color, action).ToList();
        Assert.All(events, e =>
        {
            Assert.Equal(name,  e.AgentName);
            Assert.Equal(color, e.AgentColor);
        });
    }
}
