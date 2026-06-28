using Civ2engine;
using Model;
using Model.Core;
using Model.Core.Advances;
using Model.Core.GameRules;
using Moq;

namespace Core.Tests.Governance;

public class GovernmentFunctionsTests
{
    // Governments in standard order; advance "The Republic" enables the "Republic" government.
    private static Rules MakeRules()
    {
        return new Rules
        {
            Governments = new[]
            {
                new Model.Core.GameRules.Government { Name = "Anarchy" },
                new Model.Core.GameRules.Government { Name = "Despotism" },
                new Model.Core.GameRules.Government { Name = "Monarchy", TitleMale = "King", TitleFemale = "Queen" },
                new Model.Core.GameRules.Government { Name = "Republic", TitleMale = "Consul", TitleFemale = "Consul" },
            },
            Advances = new[]
            {
                new Advance { Name = "Pottery", Index = 0 },
                new Advance { Name = "Monarchy", Index = 1 },
                new Advance { Name = "The Republic", Index = 2 },
            },
        };
    }

    private static Civilization MakeCiv() => new()
    {
        Id = 1,
        Government = GovernmentFunctions.Despotism,
        Advances = new bool[3],
        LeaderGender = 0,
    };

    [Fact]
    public void GovernmentTechIndex_MapsRepublicToTheRepublicAdvance()
    {
        var rules = MakeRules();
        Assert.Equal(1, GovernmentFunctions.GovernmentTechIndex(rules, 2)); // Monarchy gov -> Monarchy advance
        Assert.Equal(2, GovernmentFunctions.GovernmentTechIndex(rules, 3)); // Republic gov -> "The Republic" advance
        Assert.Equal(-1, GovernmentFunctions.GovernmentTechIndex(rules, 1)); // Despotism: no tech
    }

    [Fact]
    public void IsGovernmentAvailable_RequiresTheTech()
    {
        var rules = MakeRules();
        var civ = MakeCiv();
        Assert.True(GovernmentFunctions.IsGovernmentAvailable(rules, civ, GovernmentFunctions.Despotism));
        Assert.False(GovernmentFunctions.IsGovernmentAvailable(rules, civ, 2)); // no Monarchy tech yet
        civ.Advances[1] = true; // learn Monarchy
        Assert.True(GovernmentFunctions.IsGovernmentAvailable(rules, civ, 2));
    }

    [Fact]
    public void GovernmentUnlockedBy_ReturnsGovernmentForItsTech()
    {
        var rules = MakeRules();
        Assert.Equal(2, GovernmentFunctions.GovernmentUnlockedBy(rules, 1)); // Monarchy advance -> Monarchy gov
        Assert.Equal(3, GovernmentFunctions.GovernmentUnlockedBy(rules, 2)); // The Republic -> Republic gov
        Assert.Equal(-1, GovernmentFunctions.GovernmentUnlockedBy(rules, 0)); // Pottery unlocks no government
    }

    [Fact]
    public void Revolution_GoesThroughAnarchyThenAdoptsGovernment()
    {
        var rules = MakeRules();
        var civ = MakeCiv();
        civ.Advances[1] = true; // has Monarchy

        var game = new Mock<IGame>();
        game.Setup(g => g.Rules).Returns(rules);
        game.Setup(g => g.Random).Returns(new FastRandom(12345));

        GovernmentFunctions.StartRevolution(game.Object, civ);
        Assert.Equal(GovernmentFunctions.Anarchy, civ.Government);
        Assert.True(civ.AnarchyTurnsRemaining >= 1); // transition period

        GovernmentFunctions.AdoptGovernment(rules, civ, 2); // become Monarchy
        Assert.Equal(2, civ.Government);
        Assert.Equal(0, civ.AnarchyTurnsRemaining);
        Assert.Equal("King", civ.LeaderTitle);
    }
}
