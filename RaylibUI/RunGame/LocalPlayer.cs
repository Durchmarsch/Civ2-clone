using Civ2engine;
using Civ2engine.Advances;
using Civ2engine.Enums;
using Civ2engine.Events;
using Civ2engine.IO;
using Civ2engine.MapObjects;
using Model.Controls;
using Model.Core;
using Model.Core.Advances;
using Model.Core.Cities;
using Model.Core.GoodyHuts.Outcomes;
using Model.Core.Mapping;
using Model.Core.Units;
using Model.Events;
using Model.Core.Player;
using Model.Core.Production;

namespace RaylibUI.RunGame;

public class LocalPlayer : IPlayer
{
    private readonly GameScreen _gameScreen;

    public LocalPlayer(GameScreen gameScreen, Civilization civilization)
    {
        _gameScreen = gameScreen;
        Civilization = civilization;
    }

    public Civilization Civilization { get; }

    public Tile ActiveTile { get; set; }

    private Unit? _activeUnit;

    public Unit? ActiveUnit
    {
        get { return _activeUnit; }
        set
        {
            if (value == null)
            {
                _activeUnit = null;
            }
            else if (value is { TurnEnded: false, Dead: false } && value.Owner == Civilization)
            {
                if (value.CurrentLocation != null) ActiveTile = value.CurrentLocation;
                _activeUnit = value;
            }
            else
            {
#if DEBUG
                //     throw new NotSupportedException("Tried to set ended unit to active");
#endif
            }
        }
    }

    public void CivilDisorder(City city)
    {
        _gameScreen.Soundman.PlayCiv2DefaultSound("CIVDISOR");
        _gameScreen.ShowCityDialog("DISORDER", city);
    }

    public void OrderRestored(City city)
    {
        _gameScreen.ShowCityDialog("RESTORED", city);
    }

    public void WeLoveTheKingStarted(City city)
    {
        _gameScreen.Soundman.PlayCiv2DefaultSound("CRWDBUGL");
        _gameScreen.ShowCityDialog("WELOVEKING", city);
    }

    public void WeLoveTheKingCanceled(City city)
    {
        _gameScreen.ShowCityDialog("WEDONTLOVEKING", city);
    }

    public void CantMaintain(City city, Improvement cityImprovement)
    {
        _gameScreen.Soundman.PlayCiv2DefaultSound("SELL");
        _gameScreen.ShowCityDialog("INHOCK", city, [city.Name, cityImprovement.Name],
            [cityImprovement.Cost]);
    }

    public void SelectNewAdvance(List<Advance> researchPossibilities)
    {
        var activeInterface = _gameScreen.Main.ActiveInterface;
        _gameScreen.ShowPopup("RESEARCH", (s, i, arg3, arg4) =>
            {
                Civilization.ReseachingAdvance = researchPossibilities[i].Index;
            }, replaceStrings: [activeInterface.GetScientistName(Civilization.Epoch)],
            listBox: new ListboxDefinition
            {
                VerticalScrollbar = false,
                Type = ListboxType.Default,
                Rows = 16,
                Groups = researchPossibilities.Select(a => new ListboxGroup
                {
                    Elements = [new() { Icon = activeInterface.GetAdvanceImage(a), Width = 2 * 36 + 2 },
                                new() { Text = a.Name } ],
                    Height = 23
                }).ToList()
            });
    }

    public void CantProduce(City city, IProductionOrder? newItem)
    {
        _gameScreen.ShowCityDialog("BADBUILD", city);
    }

    public void CityProductionComplete(City city)
    {
        _gameScreen.ShowCityDialog("BUILT", city);
    }

    public IInterfaceCommands Ui { get; }
    public List<Unit> WaitingList { get; } = new();

    public void NotifyImprovementEnabled(TerrainImprovement improvement, int level)
    {
        var dialogKey = improvement.Levels[level].EnabledMessage;
        if (!string.IsNullOrWhiteSpace(dialogKey))
        {
            Ui.ShowDialog(dialogKey);
        }
    }

    public void MapChanged(List<Tile> tiles)
    {
        // var t = tiles.SelectMany(t => t.Map.DirectNeighbours(t));

        var allTiles = tiles
            .Concat(tiles.SelectMany(t => t.Map.DirectNeighbours(t).Where(n => n.IsVisible(_gameScreen.VisibleCivId))))
            .Distinct();
        foreach (var tile in allTiles)
        {
            _gameScreen.TileCache.Redraw(tile, _gameScreen.VisibleCivId);
        }

        _gameScreen.ForceRedraw();
    }

    public void WaitingAtEndOfTurn()
    {
        _gameScreen.ActiveMode = _gameScreen.ViewPiece;
    }

    public void NotifyAdvanceResearched(int advance)
    {
        var activeInterface = _gameScreen.Main.ActiveInterface;
        _gameScreen.ShowPopup("CIVADVANCE",
            replaceStrings: new[]
            {
                Civilization.Adjective, activeInterface.GetScientistName(Civilization.Epoch),
                _gameScreen.Game.Rules.Advances[advance].Name
            });

        // If this advance unlocks a new form of government, offer a revolution.
        var newGov = GovernmentFunctions.GovernmentUnlockedBy(_gameScreen.Game.Rules, advance);
        if (newGov >= 0 && newGov != Civilization.Government
            && Civilization.Government != GovernmentFunctions.Anarchy)
        {
            OfferRevolution(newGov);
        }
    }

    private CivDialog? _revolutionDialog;
    private CivDialog? _governmentDialog;
    private List<int>? _governmentOptions;

    private void OfferRevolution(int newGovernment)
    {
        var govName = _gameScreen.Game.Rules.Governments[newGovernment].Name;
        _revolutionDialog = new CivDialog(_gameScreen.Main, new DialogElements(new PopupBox
        {
            Title = "Revolution",
            Text = new[] { $"The people demand a new government! Shall we start a revolution to become a {govName}?" },
            Button = new[] { Labels.For(LabelIndex.Yes), Labels.For(LabelIndex.No) }
        }), HandleRevolutionChoice);
        _gameScreen.ShowDialog(_revolutionDialog, stack: true);
    }

    private void HandleRevolutionChoice(string button, int index, IList<bool>? checks,
        IDictionary<string, string>? textBoxes)
    {
        _gameScreen.CloseDialog(_revolutionDialog);
        if (button == Labels.For(LabelIndex.Yes))
        {
            GovernmentFunctions.StartRevolution(_gameScreen.Game, Civilization);
        }
    }

    public void ChooseGovernment(List<int> availableGovernments)
    {
        var governments = _gameScreen.Game.Rules.Governments;
        _governmentOptions = availableGovernments;
        _governmentDialog = new CivDialog(_gameScreen.Main, new DialogElements(new PopupBox
        {
            Title = "Select Type of Government",
            Options = availableGovernments.Select(g => governments[g].Name).ToArray(),
            Button = new[] { Labels.Ok }
        }), HandleGovernmentChosen);
        _gameScreen.ShowDialog(_governmentDialog, stack: true);
    }

    private void HandleGovernmentChosen(string button, int selectedIndex, IList<bool>? checks,
        IDictionary<string, string>? textBoxes)
    {
        _gameScreen.CloseDialog(_governmentDialog);
        if (_governmentOptions != null && selectedIndex >= 0 && selectedIndex < _governmentOptions.Count)
        {
            GovernmentFunctions.AdoptGovernment(_gameScreen.Game.Rules, Civilization, _governmentOptions[selectedIndex]);
        }
    }

    public void FoodShortage(City city)
    {
        _gameScreen.ShowCityDialog("FOODSHORTAGE", city);
    }

    public void CityDecrease(City city)
    {
        _gameScreen.ShowCityDialog("DECREASE", city);
    }

    public void TurnStart(int turnNumber)
    {
        _gameScreen.TurnStarting(turnNumber);
    }

    public void SetUnitActive(Unit? unit, bool move)
    {
        ActiveUnit = unit;
        if (_gameScreen.Game.GetActiveCiv == this.Civilization)
        {
            _gameScreen.ActiveMode = _gameScreen.Moving;
        }
    }

    public void UnitLost(Unit unit, Unit? killedBy)
    {
        //TODO: How do we use this
    }

    public void UnitsLost(List<Unit> deadUnits, Unit? killedBy)
    {
        if (deadUnits.Count > 0)
        {
            _gameScreen.Soundman.PlayCiv2DefaultSound("MEDEXPL");
        }
    }

    public void UnitMoved(Unit unit, Tile tileTo, Tile tileFrom)
    {
        if (unit.Owner == Civilization)
        {
            _gameScreen.Soundman.PlayCiv2DefaultSound("MOVPIECE");
        }
        OnUnitEvent?.Invoke(this, new MovementEventArgs(unit, tileFrom, tileTo));
    }

    public void CombatHappened(CombatEventArgs combatEventArgs)
    {
        PlayCombatSound(combatEventArgs);
        OnUnitEvent?.Invoke(this, combatEventArgs);
    }

    private void PlayCombatSound(CombatEventArgs combatEventArgs)
    {
        // The attacker's AttackSound is a full file path when the ruleset defines a
        // @SOUNDS section. MGE doesn't, so fall back to a domain-appropriate sound.
        if (!string.IsNullOrWhiteSpace(combatEventArgs.Sound))
        {
            _gameScreen.Soundman.PlaySound(combatEventArgs.Sound);
            return;
        }

        var unitTypes = _gameScreen.Game.Rules.UnitTypes;
        var type = combatEventArgs.Attacker.Type;
        var domain = type >= 0 && type < unitTypes.Length ? unitTypes[type].Domain : UnitGas.Ground;
        _gameScreen.Soundman.PlayCiv2DefaultSound(domain switch
        {
            UnitGas.Air => "AIRCOMBT",
            UnitGas.Sea => "NAVBTTLE",
            _ => "SWORDFGT"
        });
    }

    public void MoveBlocked(Unit unit, BlockedReason blockedReason)
    {
        OnUnitEvent?.Invoke(this, new MovementBlockedEventArgs(unit, blockedReason));
    }

    public event EventHandler<UnitEventArgs> OnUnitEvent;

    public void GoodyHutTriggered(Unit unit, GoodyHutOutcomeResult outcome)
    {
        var args = new GoodyHutOutcomeEventArgs(unit, outcome);
        OnUnitEvent?.Invoke(this, args);

        var popupName = outcome.OutcomeType switch
        {
            "Gold" => "SURPRISEMETALS",
            "Scrolls" => "SURPRISESCROLLS",
            "Tribe" => "SURPRISENOMADS",
            "Barbarians" => "SURPRISEBARB",
            "AbandonedVillage" => "SURPRISENOTHING",
            "Mercenaries" => "SURPRISEMERCS",
            _ => "GOODYHUT_DEFAULT"
        };

        _gameScreen.ShowPopup(popupName, replaceNumbers: [50]);
    }

    public void SelectTechFromConquest(List<Advance> techs)
    {
        var advance = _gameScreen.Game.Random.ChooseFrom(techs);
        _gameScreen.Game.GiveAdvance(advance.Index, Civilization);
        
        //TODO: Show popup
    }

    public void CityLost(City city)
    {
        //TODO: Show info ? is game over?
    }

    public void CityCaptured(City city)
    {
       //TODO: Show popup?? what does the game do here? 
    }
}