using System;
using System.Collections.Generic;
using System.Linq;
using Civ2engine.Units;
using Civ2engine.Enums;
using Civ2engine.Events;
using Civ2engine.MapObjects;
using Civ2engine.Terrains;
using Civ2engine.UnitActions;
using Model.Core.Player;
using Model.Core.Units;

namespace Civ2engine
{
    public partial class Game
    {
        /// <summary>
        /// This is now only used for lua script integration for other events raise them on the player version
        /// </summary>
        public event EventHandler<UnitEventArgs> OnUnitEvent;
        internal event EventHandler<CivEventArgs> OnCivEvent;

        private readonly int[] _doNothingOrders = { (int)OrderType.Fortified, (int)OrderType.Sleep };

        // Choose next unit for orders. If all units ended turn, update cities.
        public void ChooseNextUnit()
        {
            var units = _activeCiv.Units.Where(u => !u.Dead).ToList();

            var player = Players[_activeCiv.Id];
            
            //Look for units on this square or neighbours of this square
            
            var nextUnit = NextUnit(player, units);

            // End turn if no units awaiting orders
            if (nextUnit == null)
            {
                var anyUnitsMoved = units.Any(u => u.MovePointsLost > 0);
                if ((!anyUnitsMoved || Options.AlwaysWaitAtEndOfTurn))
                {
                    Players[_activeCiv.Id].WaitingAtEndOfTurn();
                }
                else
                {
                    if (ProcessEndOfTurn())
                    {
                        ChoseNextCiv();
                    }
                }
            }
            else
            {
                //TODO: determine the true values of these extra props
                OnUnitEvent?.Invoke(this, new ActivationEventArgs(unit: nextUnit, userInitiated: true, reactivation: false));
                player.SetUnitActive(nextUnit, true);
                // If the player immediately moved the unit it might be already dead or moved so choose again.
                // Use MovePoints <= 0 (not MovePointsLost == MaxMovePoints): moving onto high-cost terrain
                // (forest/hills/mountains) overshoots, leaving MovePointsLost > MaxMovePoints, so the exact
                // equality failed and the next unit was never chosen -> the turn hung on a spent unit.
                if (nextUnit.Dead || nextUnit.MovePoints <= 0)
                {
                    ChooseNextUnit();
                }
            }
        }

        private Unit? NextUnit(IPlayer player, List<Unit> units)
        {
            if (player.WaitingList is { Count: > 0 })
            {
                return
                    ActiveTile.UnitsHere.FirstOrDefault(u => u.AwaitingOrders && !player.WaitingList.Contains(u)) ??
                    ActiveTile
                        .Neighbours()
                        .SelectMany(
                            t => t.UnitsHere.Where(u =>
                                u.Owner == _activeCiv && u.AwaitingOrders && !player.WaitingList.Contains(u)))
                        .FirstOrDefault() ??
                    units.FirstOrDefault(u => u.AwaitingOrders && !player.WaitingList.Contains(u)) ??
                    ResetWaiting(player);

            }

            return ActiveTile.UnitsHere.FirstOrDefault(u => u.AwaitingOrders) ??
                   ActiveTile
                       .Neighbours()
                       .SelectMany(
                           t => t.UnitsHere.Where(u => u.Owner == _activeCiv && u.AwaitingOrders))
                       .FirstOrDefault() ?? units.FirstOrDefault(u => u.AwaitingOrders);

        }

        private Unit ResetWaiting(IPlayer player)
        {
            var unit = player.WaitingList[0];
            player.WaitingList.Clear();
            return unit;
        }

        public bool ProcessEndOfTurn()
        {
            var player = Players[_activeCiv.Id];
            foreach (var unit in _activeCiv.Units)
            {
                if (unit is { MovePoints: > 0, CurrentLocation: not null } && !_doNothingOrders.Contains(unit.Order))
                {
                    switch ((OrderType)unit.Order)
                    {
                        case OrderType.Fortify:
                            unit.Order = (int)OrderType.Fortified;
                            unit.MovePointsLost = unit.MovePoints;
                            break;
                        case OrderType.GoTo:
                            // Advance the unit toward its GoTo destination this turn.
                            MovementFunctions.ContinueGoTo(this, unit);

                            // If it arrived / the goal was unreachable, ContinueGoTo cleared the
                            // order. With movement still left the unit now awaits new orders, so
                            // hand control back to the player; otherwise it's done for this turn.
                            if (unit is { Order: (int)OrderType.NoOrders, MovePoints: > 0 })
                            {
                                player.SetUnitActive(unit, true);
                                return false;
                            }

                            break;
                        default:
                        {
                            unit.ProcessOrder();

                            if (TerrainImprovements.TryGetValue(unit.Building, out var improvement))
                            {
                                var activeUnit = this.CheckConstruction(unit.CurrentLocation, improvement)
                                    .FirstOrDefault(u => u.MovePoints > 0);
                                if (activeUnit != null)
                                {
                                    player.SetUnitActive(activeUnit, true);
                                    return false;
                                }
                            }

                            break;
                        }
                    }
                }
            }

            return true;
        }
    }
}
