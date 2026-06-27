using System.Collections.Generic;
using System.Linq;
using Civ2engine.Advances;
using Model.Constants;
using Model.Core;
using Model.Core.Advances;
using Model.Core.Cities;
using Model.Core.Production;

namespace Civ2engine.Production
{
    public static class ProductionPossibilities
    {
        private static List<IProductionOrder>[] _availableProducts;

        // Wonders are world-unique. Improvement.Type of every wonder already built in the
        // world, so it can never be built (or kept in production) anywhere again.
        private static readonly HashSet<int> _builtWonders = new();

        public static void InitializeProductionLists(IEnumerable<Civilization> civs, IProductionOrder[] possibleOrders)
        {
            var civList = civs.ToList();
            var orders = possibleOrders
                .Where(o => o.RequiredTech != AdvancesConstants.No && o.ExpiresTech != AdvancesConstants.No).ToList();

            _availableProducts = civList.Select(c =>
                    orders.Where(o =>
                        (o.ExpiresTech == AdvancesConstants.Nil || (o.ExpiresTech < c.Advances.Length && !c.Advances[o.ExpiresTech])) &&
                        (o.RequiredTech == AdvancesConstants.Nil || (o.RequiredTech < c.Advances.Length && c.Advances[o.RequiredTech]))).ToList())
                .ToArray();

            // Seed the built-wonder set from any wonders that already exist (e.g. when
            // loading a save) so they stay unbuildable.
            _builtWonders.Clear();
            foreach (var wonder in civList.SelectMany(c => c.Cities)
                         .SelectMany(city => city.Improvements).Where(i => i.IsWonder))
            {
                _builtWonders.Add(wonder.Type);
            }
        }

        /// <summary>A wonder, once built anywhere, can never be built again (world-unique).</summary>
        public static bool WonderAlreadyBuilt(int improvementType) => _builtWonders.Contains(improvementType);

        /// <summary>
        /// Record a wonder as built and pull it from every civ's production list so no one
        /// else can build it; cities mid-build switch production at the next turn.
        /// </summary>
        public static void RegisterWonderBuilt(IProductionOrder wonderOrder, int improvementType)
        {
            _builtWonders.Add(improvementType);
            if (_availableProducts != null)
            {
                foreach (var list in _availableProducts)
                {
                    list.Remove(wonderOrder);
                }
            }
        }

        public static void AddItems(int targetCiv, IEnumerable<IProductionOrder> items)
        {
            _availableProducts[targetCiv].AddRange(items);
        }
        
        public static void RemoveItems(int targetCiv, IEnumerable<IProductionOrder> items)
        {
            var itemList = items.ToList();
            if (itemList.Count > 0)
            {
                _availableProducts[targetCiv].RemoveAll(i => itemList.Contains(i));
            }
        }

        public static bool ProductionValid(City city)
        {
            return _availableProducts[city.OwnerId].Contains(city.ItemInProduction) && city.ItemInProduction.IsValidBuild(city);
        }

        public static IProductionOrder? AutoNext(City city)
        {
            return _availableProducts[city.OwnerId]
                .Where(p => p.RequiredTech == city.ItemInProduction.ExpiresTech && p.Type == city.ItemInProduction.Type)
                .MinBy(p => p.Cost);
        }

        public static Improvement? FindByEffect(int targetCiv, Effects effect)
        {
            return _availableProducts[targetCiv].OfType<BuildingProductionOrder>()
                .Where(p => p.Improvement.Effects.ContainsKey(effect)).Select(o => o.Improvement).FirstOrDefault();
        }

        public static IList<IProductionOrder> GetAllowedProductionOrders(City thisCity)
        {
            return _availableProducts[thisCity.OwnerId].Where(i => i.IsValidBuild(thisCity)).ToList();
        }
    }
}