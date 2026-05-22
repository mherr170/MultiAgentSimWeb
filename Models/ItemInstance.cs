namespace MultiAgentSimWeb.Models;

public class ItemInstance
{
    public string DefinitionId  { get; }
    public Guid   InstanceId    { get; } = Guid.NewGuid();
    public int    UsesRemaining { get; set; }

    public ItemDefinition Definition  => ItemRegistry.Get(DefinitionId);
    public string          DisplayName =>
        Definition.MaxUses > 0 ? $"{Definition.Name} ({UsesRemaining} uses)" : Definition.Name;

    public ItemInstance(string definitionId)
    {
        DefinitionId  = definitionId;
        UsesRemaining = ItemRegistry.Get(definitionId).MaxUses;
    }
}
