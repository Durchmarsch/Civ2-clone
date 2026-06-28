using Civ2engine.Production;
using Model.Core;
using Model.Core.Advances;
using Model.Core.Cities;
using Model.Core.Production;
using Model.Core.Units;
using Civ2engine.Enums;

namespace Core.Tests.Production;

public class ProductionListTests
{
    // Regression: after loading a save the civ's Advances array is clamped (trailing falses
    // trimmed), so it can be shorter than a unit's ExpiresTech (Until) index. The obsolete
    // check used to require ExpiresTech < Advances.Length, wrongly marking every
    // not-yet-obsolete unit as obsolete -> cities could only build improvements, not units.
    [Fact]
    public void UnitWithExpiryTechBeyondClampedAdvances_IsBuildable()
    {
        // A ground unit that becomes obsolete at a high tech index (e.g. 30), no prerequisite.
        var unitDef = new UnitDefinition
        {
            Name = "Warriors",
            Domain = UnitGas.Ground,
            Prereq = AdvancesConstants.Nil,   // no tech required to build
            Until = 30,                       // obsolete only at tech #30
            Cost = 1,
            Flags = new bool[20],
        };
        var order = new UnitProductionOrder(unitDef, 0);

        // Civ whose Advances array is short (clamped) -- length 5, well below the Until index 30.
        var civ = new Civilization { Id = 0, Advances = new bool[5] };
        var city = new City { Owner = civ };

        ProductionPossibilities.InitializeProductionLists(new[] { civ }, new IProductionOrder[] { order });

        var buildable = ProductionPossibilities.GetAllowedProductionOrders(city);
        Assert.Contains(order, buildable);
    }
}
