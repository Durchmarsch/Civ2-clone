using Civ2engine.Advances;
using Model.Core;
using Model.Core.Cities;

namespace Civ2engine
{
    public static class WonderEffects
    {
        /// <summary>
        /// One-time effects fired the moment a wonder is completed — the things the
        /// data-driven Effects system in improvements.lua cannot express (granting
        /// techs, revealing the map, ...). Matched by name so it stays independent of
        /// the MGE/ToT improvement indices.
        /// </summary>
        public static void ApplyOnBuild(Game game, City city, Improvement wonder)
        {
            switch (wonder.Name)
            {
                case "Apollo Program":
                    // Reveals the entire map.
                    foreach (var map in game.Maps)
                    {
                        map.MapRevealed = true;
                    }
                    break;
                case "Darwin's Voyage":
                    // Grants the builder two immediate technology advances.
                    GiveFreeAdvances(game, city.Owner, 2);
                    break;
            }
        }

        private static void GiveFreeAdvances(Game game, Civilization civ, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var available = AdvanceFunctions.CalculateAvailableResearch(game, civ);
                if (available.Count == 0)
                {
                    break;
                }

                var pick = available[game.Random.Next(available.Count)];
                game.GiveAdvance(pick.Index, civ);
            }
        }
    }
}
