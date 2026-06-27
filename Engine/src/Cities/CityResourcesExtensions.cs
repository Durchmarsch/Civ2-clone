using System;
using System.Linq;
using System.Security.AccessControl;
using Model.Constants;
using Model.Core.Cities;

namespace Civ2engine;

public static class CityResourcesExtensions
{
    private static decimal GetMultiplier(this City city, Effects effect)
    {
        return (100 + city.EffectImprovements()
            .Where(i => i.Effects.ContainsKey(effect))
            .Select(b => b.Effects[effect]).Sum()) / 100m;
    }

    private static int GetBaseScience(this City city)
    {
        return city.Trade * city.Owner.ScienceRate / 100;
    }

    public static int GetScience(this City city)
    {
        var multiplier = city.GetMultiplier(Effects.ScienceMultiplier);
        // Isaac Newton's College doubles the science bonus from buildings in its city.
        if (city.EffectImprovements().Any(i => i.Name == "Isaac Newton's College"))
        {
            multiplier += multiplier - 1m;
        }

        return (int)(city.GetBaseScience() * multiplier);
    }

    private static int GetBaseLuxury(this City city)
    {
        return city.Trade * city.Owner.LuxRate / 100;
    }

    public static int GetLuxury(this City city)
    {
        return (int)(city.GetBaseLuxury() * city.GetMultiplier(Effects.LuxMultiplier));
    }

    /// <summary>
    /// Formula should always round excess into tax
    /// </summary>
    public static int GetTax(this City city)
    {
        return (int)((city.Trade - GetBaseLuxury(city) - GetBaseScience(city)) *
                     GetMultiplier(city, Effects.TaxMultiplier));
    }

    public static int GetResourceValues(this City city, string name)
    {
        return name switch
        {
            "Science" => city.GetScience(),
            "Lux" => city.GetLuxury(),
            "Tax" => city.GetTax(),
            "Shields" => city.Production,
            "Food" => city.SurplusHunger,
            _ => throw new NotSupportedException()
        };
    }
    
    public static ResourceValues GetConsumableResourceValues(this City city, string resourceName)
    {
        switch (resourceName)
        {
            case "Food":
                return city.SurplusHunger > 0
                    ? new ResourceValues(consumption: city.Food, surplus: city.SurplusHunger, loss: 0)
                    : new ResourceValues(consumption: city.Food, surplus: 0, loss: -city.SurplusHunger);
            case "Shields":
                return new ResourceValues(consumption: city.Support, surplus: city.Production, loss: city.Waste);
            case "Trade":
                return new ResourceValues(consumption: city.Trade, surplus: 0, loss: city.Corruption);
            default:
                throw new NotImplementedException();
        }
    }
}