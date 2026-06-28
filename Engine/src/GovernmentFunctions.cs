using System.Collections.Generic;
using System.Linq;
using Model.Core;
using Model.Core.GameRules;

namespace Civ2engine
{
    /// <summary>
    /// Government availability and the revolution / anarchy transition.
    /// Government index 0 = Anarchy, 1 = Despotism (both always available); the others each need
    /// an enabling advance whose name matches the government (with "Republic" ↔ "The Republic").
    /// </summary>
    public static class GovernmentFunctions
    {
        public const int Anarchy = 0;
        public const int Despotism = 1;

        // Minimum / maximum length of the anarchy period when a revolution starts.
        private const int MinAnarchyTurns = 1;
        private const int MaxAnarchyTurns = 3;

        // Government name -> the name of the advance that unlocks it. Governments not listed
        // (Anarchy, Despotism) need no advance.
        private static readonly Dictionary<string, string> GovernmentAdvanceNames = new()
        {
            ["Monarchy"] = "Monarchy",
            ["Communism"] = "Communism",
            ["Fundamentalism"] = "Fundamentalism",
            ["Republic"] = "The Republic",
            ["Democracy"] = "Democracy",
        };

        /// <summary>Index of the advance that unlocks government <paramref name="govIndex"/>, or -1 if none.</summary>
        public static int GovernmentTechIndex(Rules rules, int govIndex)
        {
            if (govIndex < 0 || govIndex >= rules.Governments.Length) return -1;
            if (!GovernmentAdvanceNames.TryGetValue(rules.Governments[govIndex].Name, out var advanceName))
            {
                return -1;
            }

            for (var i = 0; i < rules.Advances.Length; i++)
            {
                if (rules.Advances[i].Name == advanceName) return i;
            }

            return -1;
        }

        /// <summary>Can the civ currently adopt the given government (Anarchy/Despotism always; others need the tech)?</summary>
        public static bool IsGovernmentAvailable(Rules rules, Civilization civ, int govIndex)
        {
            if (govIndex == Anarchy || govIndex == Despotism) return true;
            var tech = GovernmentTechIndex(rules, govIndex);
            return tech >= 0 && tech < civ.Advances.Length && civ.Advances[tech];
        }

        /// <summary>The governments the civ may switch to (Despotism + every unlocked one; not Anarchy).</summary>
        public static List<int> AvailableGovernments(Rules rules, Civilization civ)
        {
            var result = new List<int>();
            for (var g = 0; g < rules.Governments.Length; g++)
            {
                if (g == Anarchy) continue;
                if (IsGovernmentAvailable(rules, civ, g)) result.Add(g);
            }

            return result;
        }

        /// <summary>
        /// If the just-researched advance unlocks a government the civ couldn't form before, return
        /// that government's index; otherwise -1.
        /// </summary>
        public static int GovernmentUnlockedBy(Rules rules, int advanceIndex)
        {
            for (var g = 0; g < rules.Governments.Length; g++)
            {
                if (GovernmentTechIndex(rules, g) == advanceIndex) return g;
            }

            return -1;
        }

        /// <summary>Begin a revolution: drop into Anarchy for a short transition period.</summary>
        public static void StartRevolution(IGame game, Civilization civ)
        {
            civ.Government = Anarchy;
            civ.AnarchyTurnsRemaining = game.Random.Next(MinAnarchyTurns, MaxAnarchyTurns + 1);
            UpdateLeaderTitle(game.Rules, civ);
        }

        /// <summary>Adopt the chosen government (called once the anarchy period ends).</summary>
        public static void AdoptGovernment(Rules rules, Civilization civ, int govIndex)
        {
            civ.Government = govIndex;
            civ.AnarchyTurnsRemaining = 0;
            UpdateLeaderTitle(rules, civ);
        }

        private static void UpdateLeaderTitle(Rules rules, Civilization civ)
        {
            if (civ.Government < 0 || civ.Government >= rules.Governments.Length) return;
            var gov = rules.Governments[civ.Government];
            civ.LeaderTitle = civ.LeaderGender == 0 ? gov.TitleMale : gov.TitleFemale;
        }
    }
}
