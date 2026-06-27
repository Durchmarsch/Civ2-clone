using Civ2engine.Advances;
using Civ2engine.Production;
using Model.Core.Player;

namespace Civ2engine
{
    public static class GameTurn
    {
        /// <summary>
        /// Updates the stats of all cities for the active player's turn.
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <param name="player">The active player.</param>
        /// <remarks>
        /// This method performs the following actions for each city:
        /// - Updates food storage and city size based on surplus/deficit
        /// - Handles civil disorder and "We Love the King Day" events
        /// - Manages production and item completion
        /// - Collects taxes and pays for city improvements
        /// - Contributes to research progress
        /// </remarks>
        public static void CitiesTurn(this Game game, IPlayer player)
        {
            var activeCiv = game.GetActiveCiv;
            var currentScienceCost = AdvanceFunctions.CalculateScienceCost(game, activeCiv);

            var rules = game.Rules;
            
            var foodRows = rules.Cosmic.RowsFoodBox;
            var shieldRows = rules.Cosmic.RowsShieldBox;

            foreach (var city in activeCiv.Cities)
            {
                city.ImprovementSold = false;

                // Change food in storage
                city.FoodInStorage += city.SurplusHunger;

                var shields = city.Production;

                //TODO: Combine these calls
                var tax = city.GetTax();
                var science = city.GetScience();

                // Change city size
                if (city.FoodInStorage < 0)
                {
                    city.FoodInStorage = 0;
                    city.ShrinkCity(game);

                    game.UpdateTiles([city.Location]);
                    player.CityDecrease(city);
                }
                else if (city.SurplusHunger < 0 && city.FoodInStorage + city.SurplusHunger < 0)
                {
                    player.FoodShortage(city);
                }
                else
                {
                    var maxFood = (city.Size + 1) * foodRows;
                    if (city.FoodInStorage > maxFood)
                    {
                        city.GrowCity(game);
                        city.ResetFoodStorage(foodRows);
                    }
                }

                if (city.UnhappyCitizens > 0)
                {
                    if (city.WeLoveKingDay)
                    {
                        player.WeLoveTheKingCanceled(city);
                        city.WeLoveKingDay = false;
                    }

                    if (city.UnhappyCitizens > city.HappyCitizens)
                    {
                        player.CivilDisorder(city);
                        city.CivilDisorder = true;
                        continue;
                    }

                    if (city.CivilDisorder)
                    {
                        city.CivilDisorder = false;
                        player.OrderRestored(city);
                    }

                }
                else
                {
                    if (city.CivilDisorder)
                    {
                        city.CivilDisorder = false;
                        player.OrderRestored(city);
                    }

                    if (city.HappyCitizens >= city.Size - city.UnhappyCitizens - city.HappyCitizens)
                    {
                        if (!city.WeLoveKingDay)
                        {
                            player.WeLoveTheKingStarted(city);
                        }

                        city.WeLoveKingDay = true;
                    }
                }

                if (!ProductionPossibilities.ProductionValid(city))
                {
                    var newItem = ProductionPossibilities.AutoNext(city);

                    player.CantProduce(city, newItem);

                    if (newItem != null)
                    {
                        city.ItemInProduction = newItem;
                    }
                }

                city.ShieldsProgress += shields;


                if (city.ShieldsProgress >= city.ItemInProduction.Cost * shieldRows)
                {
                    if (city.ItemInProduction.CompleteProduction(city, rules))
                    {
                        if (city.ItemInProduction is BuildingProductionOrder { Improvement: { IsWonder: true } wonder })
                        {
                            WonderEffects.ApplyOnBuild(game, city, wonder);
                        }

                        city.ShieldsProgress = 0;
                        player.CityProductionComplete(city);
                    }
                }

                activeCiv.Money += tax;

                foreach (var cityImprovement in city.Improvements)
                {
                    if (cityImprovement.Upkeep > 0)
                    {
                        if (activeCiv.Money >= cityImprovement.Upkeep)
                        {
                            activeCiv.Money -= cityImprovement.Upkeep;
                        }
                        else
                        {
                            //Sell it !!
                            city.SellImprovement(cityImprovement);
                            activeCiv.Money += cityImprovement.Cost;
                            player.CantMaintain(city, cityImprovement);
                        }
                    }
                }

                // Accumulate this city's research output (may be 0 for low-trade cities).
                if (science > 0)
                {
                    activeCiv.Science += science;
                }
            }

            // Research target / completion is handled once per civ per turn, NOT per city.
            // Only civs with at least one city can research (this also skips the barbarian
            // "civ", which has no cities and no AllowedAdvanceGroups set up):
            //  - If we have no research target, prompt for one. This must NOT be gated on this
            //    turn's science output, otherwise a civ whose cities each produce 0 beakers
            //    (e.g. 1 trade * 60% truncates to 0) would never be asked to pick research.
            //  - Otherwise, once enough beakers have accumulated, complete the advance.
            if (activeCiv.Cities.Count > 0)
            {
                // DIAGNOSTIC (research-stopped investigation) -- remove after diagnosis
                if (activeCiv == game.GetPlayerCiv)
                {
                    var poss = AdvanceFunctions.CalculateAvailableResearch(game, activeCiv).Count;
                    System.Console.WriteLine($"[RESDIAG] researching={activeCiv.ReseachingAdvance} science={activeCiv.Science} cost={currentScienceCost} possibilities={poss}");
                }

                if (activeCiv.ReseachingAdvance < 0)
                {
                    var researchPossibilities = AdvanceFunctions.CalculateAvailableResearch(game, activeCiv);
                    if (researchPossibilities.Count > 0)
                    {
                        System.Console.WriteLine($"[RESDIAG] -> SelectNewAdvance ({researchPossibilities.Count} options)"); // DIAGNOSTIC
                        player.SelectNewAdvance(researchPossibilities);
                    }
                }
                else if (currentScienceCost > 0 && currentScienceCost <= activeCiv.Science)
                {
                    System.Console.WriteLine($"[RESDIAG] -> COMPLETE advance {activeCiv.ReseachingAdvance}"); // DIAGNOSTIC
                    player.NotifyAdvanceResearched(activeCiv.ReseachingAdvance);
                    game.GiveAdvance(activeCiv.ReseachingAdvance, activeCiv);
                    activeCiv.Science -= currentScienceCost;
                }
            }
        }
    }
}
