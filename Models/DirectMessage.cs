namespace MultiAgentSimWeb.Models;

public class DirectMessage
{
    public string FromAgent { get; init; } = "";
    public string ToAgent   { get; init; } = "";
    public string Message   { get; init; } = "";
    public int    Round     { get; init; }
}

/// One line in an ongoing back-and-forth conversation.
public record ConversationLine(string Speaker, string Text);
