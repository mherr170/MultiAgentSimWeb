using MultiAgentSimWeb.Models;

namespace MultiAgentSimWebTests;

public class AgentMemoryTests
{
    // ── Add / Recent ──────────────────────────────────────────────────────────

    [Fact]
    public void Add_SingleEntry_AppearsInRecent()
    {
        var mem = new AgentMemory();
        mem.Add("Found water near the river.");
        Assert.Single(mem.Recent);
        Assert.Equal("Found water near the river.", mem.Recent[0]);
    }

    [Fact]
    public void Add_MultipleEntries_PreservesOrder()
    {
        var mem = new AgentMemory();
        mem.Add("first");
        mem.Add("second");
        mem.Add("third");
        Assert.Equal("first",  mem.Recent[0]);
        Assert.Equal("second", mem.Recent[1]);
        Assert.Equal("third",  mem.Recent[2]);
    }

    [Fact]
    public void Add_ExactlyAtCapacity_AllEntriesRetained()
    {
        var mem = new AgentMemory();
        for (int i = 1; i <= 8; i++)
            mem.Add($"entry {i}");
        Assert.Equal(8, mem.Recent.Count);
    }

    [Fact]
    public void Add_OneOverCapacity_OldestEntryEvicted()
    {
        var mem = new AgentMemory();
        for (int i = 1; i <= 8; i++)
            mem.Add($"entry {i}");
        mem.Add("entry 9");

        Assert.Equal(8, mem.Recent.Count);
        Assert.DoesNotContain("entry 1", mem.Recent);
        Assert.Equal("entry 9", mem.Recent[^1]);
    }

    [Fact]
    public void Add_ManyOverCapacity_CapNeverExceeded()
    {
        var mem = new AgentMemory();
        for (int i = 1; i <= 20; i++)
            mem.Add($"entry {i}");
        Assert.Equal(8, mem.Recent.Count);
    }

    [Fact]
    public void Add_ManyOverCapacity_RetainsNewestEntries()
    {
        var mem = new AgentMemory();
        for (int i = 1; i <= 20; i++)
            mem.Add($"entry {i}");

        // Only the last 8 should survive
        for (int i = 13; i <= 20; i++)
            Assert.Contains($"entry {i}", mem.Recent);
        for (int i = 1; i <= 12; i++)
            Assert.DoesNotContain($"entry {i}", mem.Recent);
    }

    [Fact]
    public void Add_ManyOverCapacity_OrderIsChronological()
    {
        var mem = new AgentMemory();
        for (int i = 1; i <= 20; i++)
            mem.Add($"entry {i}");

        Assert.Equal("entry 13", mem.Recent[0]);
        Assert.Equal("entry 20", mem.Recent[^1]);
    }

    // ── Format ────────────────────────────────────────────────────────────────

    [Fact]
    public void Format_EmptyMemory_ReturnsEmptyString()
    {
        var mem = new AgentMemory();
        Assert.Equal("", mem.Format());
    }

    [Fact]
    public void Format_NonEmpty_ContainsHeader()
    {
        var mem = new AgentMemory();
        mem.Add("something happened");
        Assert.Contains("MEMORIES", mem.Format());
    }

    [Fact]
    public void Format_NonEmpty_ContainsAllEntries()
    {
        var mem = new AgentMemory();
        mem.Add("saw Bob at the fountain");
        mem.Add("picked up a first aid kit");

        var output = mem.Format();
        Assert.Contains("saw Bob at the fountain",  output);
        Assert.Contains("picked up a first aid kit", output);
    }

    [Fact]
    public void Format_EachEntryPrefixedWithDash()
    {
        var mem = new AgentMemory();
        mem.Add("entry A");
        var lines = mem.Format().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // All non-header lines should start with "- "
        foreach (var line in lines.Skip(1))
            Assert.StartsWith("- ", line);
    }

    [Fact]
    public void Format_DoesNotTrailWithNewline()
    {
        var mem = new AgentMemory();
        mem.Add("test");
        Assert.False(mem.Format().EndsWith('\n'));
    }
}
