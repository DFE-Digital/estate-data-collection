namespace EstateDataCollection.Domain.Schema;

public enum SchemaLifecycleState
{
    Unassigned = 0,
    
    Draft = 1,
    Validated = 2,
    Scheduled = 3,
    Active = 4,
    Deprecated = 5,
    Retired = 6
}