namespace Meitou.Simulation;

public sealed partial class PopulationSystem
{
    internal void RefreshCutEquipment(CharacterCold cold) => assembly.RefreshEquipment(cold);
}
