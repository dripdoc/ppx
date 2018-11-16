namespace PillPackEx
{
    internal enum GenericEquivalentStatus
    {
        IsGeneric,
        GenericEquivalentAvailable,
        NoAvailableEquivalent
    }

    internal struct GenericEquivalentData
    {
        internal GenericEquivalentStatus status{get; set;}
        internal string GenericId  {get; set;}
        
    }
}