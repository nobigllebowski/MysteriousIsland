namespace ForgottenIsle.Core.Items
{
    /// <summary>
    /// Which two items make a third, and what the player is told when they do not.
    /// </summary>
    /// <remarks>
    /// WHY A TABLE AND NOT A CRAFTING SYSTEM. There are three combinations in this act and there
    /// will not be thirty. Each one is a specific thing a specific person would do with two
    /// specific objects, written by hand because it has a reason in the fiction — winding wet tape
    /// onto a dry core is not "recipe 7", it is what you do to tape that has been in a channel.
    /// A general system would let anyone add a hundred more, and a hundred more is the crafting
    /// grind this game is explicitly not.
    /// <para>
    /// Order does not matter: A with B is the same act as B with A, and a player who tries it the
    /// other way round has not made a mistake.
    /// </para>
    /// </remarks>
    public static class Combinations
    {
        /// <summary>One combination: two inputs, one output, and the line for a near miss.</summary>
        public readonly struct Recipe
        {
            /// <summary>First input.</summary>
            public readonly string A;

            /// <summary>Second input.</summary>
            public readonly string B;

            /// <summary>What both are replaced by.</summary>
            public readonly string Result;

            /// <param name="a">First input.</param>
            /// <param name="b">Second input.</param>
            /// <param name="result">What both become.</param>
            public Recipe(string a, string b, string result)
            {
                A = a;
                B = b;
                Result = result;
            }

            /// <summary>True when these two ids are this recipe's inputs, in either order.</summary>
            public bool Matches(string first, string second)
            {
                return (A == first && B == second) || (A == second && B == first);
            }
        }

        private static readonly Recipe[] All =
        {
            // Wet tape binds on its own swollen core. Off it, onto something dry that will not
            // swell, and it will run. The spindle is on the shore because the net drum used one.
            new Recipe(ItemIds.WaterloggedReel, ItemIds.DrySpindle, ItemIds.ReboundReel),

            // The rope is sun-rotted to felt; teased apart with the blade it is a nest of fibre
            // that holds an ember, and being plastic it catches from a spark far more readily
            // than grass. The tool survives (ItemIds.IsTool); the rope does not.
            new Recipe(ItemIds.PolyRope, ItemIds.Multitool, ItemIds.PolyFibre)
        };

        /// <summary>Finds what these two items make.</summary>
        /// <param name="first">One carried item id.</param>
        /// <param name="second">Another carried item id.</param>
        /// <param name="result">The item both become, when there is one.</param>
        /// <returns>True when a recipe matched.</returns>
        public static bool TryCombine(string first, string second, out string result)
        {
            result = null;

            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || first == second)
            {
                return false;
            }

            for (var i = 0; i < All.Length; i++)
            {
                if (All[i].Matches(first, second))
                {
                    result = All[i].Result;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Number of recipes, so a test can assert the table was not silently emptied.</summary>
        public static int RecipeCount => All.Length;
    }
}
