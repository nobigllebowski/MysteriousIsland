using ForgottenIsle.Core.Items;

namespace ForgottenIsle.Core.Fire
{
    /// <summary>What is laid in the hollow to take a spark.</summary>
    public enum Tinder : byte
    {
        None = 0,

        /// <summary>Dry grass. Flares for a second and dies. The authored failure.</summary>
        Grass = 1,

        /// <summary>Teased poly fibre. Holds an ember.</summary>
        Fibre = 2
    }

    /// <summary>What happened when an item met a fire site.</summary>
    public enum FireAct : byte
    {
        /// <summary>The item means nothing here.</summary>
        Nothing = 0,
        GrassLaid = 1,
        FibreLaid = 2,
        WoodStacked = 3,
        PanelPlaced = 4,

        /// <summary>The site is already out of the wind; the panel stays in the bag.</summary>
        PanelPointless = 5,

        /// <summary>The fire is burning; laying more is refused.</summary>
        AlreadyLit = 6,

        /// <summary>Wet wood on an unlit site: nothing lights that.</summary>
        WetWoodRefused = 7,

        /// <summary>Wet wood by a burning fire: it dries in the warm zone.</summary>
        WoodDrying = 8,
        KelpRefused = 9,

        /// <summary>Chert with no multitool carried: nothing to strike it on.</summary>
        NoSpine = 10,

        /// <summary>Sparks with no tinder laid: bright, fast, onto sand.</summary>
        SparksOnSand = 11,

        /// <summary>Sparks into grass: a flare, and gone. The grass is spent.</summary>
        GrassFlared = 12,

        /// <summary>An ember in the fibre with no wood to give it: it holds eight seconds and dies.</summary>
        EmberStarved = 13,

        /// <summary>Lit in the open: three seconds, then the wind puts it out. The kit stays.</summary>
        BlewOut = 14,

        /// <summary>Fire.</summary>
        Lit = 15,

        /// <summary>Grass offered where fibre is already laid: the better tinder stays.</summary>
        GrassPointless = 16
    }

    /// <summary>
    /// One place a fire can be laid, and the rules of the dry-fire problem applied to it.
    /// </summary>
    /// <remarks>
    /// The design's first real puzzle (<c>design/04-first-30-minutes.md</c> §7:10): spark,
    /// tinder, shelter, and the player is missing all three. Engine-free so the table of
    /// outcomes -- including the five authored failures -- is pinned by tests rather than by
    /// playing it. The grammar is the game's aimed use: an item held, a site in front of you.
    /// The design's downward strike swipe is a use of the chert here; the gesture is a later
    /// polish item, the rule is not.
    /// <para>
    /// Nothing here punishes. Sparks on sand cost nothing, an ember that starves leaves the
    /// fibre where it was, a blow-out leaves the whole kit laid. The one thing spent by failing
    /// is the grass, which was never going to work and is the lesson (§4.2).
    /// </para>
    /// </remarks>
    public sealed class FireSiteState
    {
        /// <param name="id">A <c>ContentIds</c> fire site id.</param>
        /// <param name="inLee">True when the site is in a hull's wind shadow by construction.</param>
        public FireSiteState(string id, bool inLee)
        {
            Id = id ?? string.Empty;
            InLee = inLee;
        }

        /// <summary>The site's content id.</summary>
        public string Id { get; }

        /// <summary>Sheltered by where it is: the lee of the landing craft.</summary>
        public bool InLee { get; }

        /// <summary>The fibreglass panel is wedged upright on the windward side.</summary>
        public bool PanelPlaced { get; private set; }

        /// <summary>What is laid to take the spark.</summary>
        public Tinder Tinder { get; private set; }

        /// <summary>An armful of dry wood is stacked.</summary>
        public bool HasWood { get; private set; }

        /// <summary>Burning.</summary>
        public bool IsLit { get; private set; }

        /// <summary>Times a fire lit here and the wind put it out.</summary>
        public int BlowOuts { get; private set; }

        /// <summary>Out of the wind, by place or by panel.</summary>
        public bool Sheltered => InLee || PanelPlaced;

        /// <summary>Something is laid here worth carrying somewhere better.</summary>
        public bool HasKit => Tinder != Tinder.None || HasWood;

        /// <summary>
        /// Applies an item to the site.
        /// </summary>
        /// <param name="itemId">The carried item.</param>
        /// <param name="hasMultitool">Whether the multitool is carried: the chert needs its spine.</param>
        public FireAct Apply(string itemId, bool hasMultitool)
        {
            switch (itemId)
            {
                case ItemIds.DryGrass:
                    if (IsLit)
                    {
                        return FireAct.AlreadyLit;
                    }

                    if (Tinder == Tinder.Fibre)
                    {
                        // The one rope's fibre must never be replaced by the tinder that cannot
                        // work: a flare would take the fibre with it, and the run with the fibre.
                        return FireAct.GrassPointless;
                    }

                    Tinder = Tinder.Grass;
                    return FireAct.GrassLaid;

                case ItemIds.PolyFibre:
                    if (IsLit)
                    {
                        return FireAct.AlreadyLit;
                    }

                    Tinder = Tinder.Fibre;
                    return FireAct.FibreLaid;

                case ItemIds.DriftwoodDry:
                    if (IsLit)
                    {
                        return FireAct.AlreadyLit;
                    }

                    HasWood = true;
                    return FireAct.WoodStacked;

                case ItemIds.DriftwoodWet:
                    return IsLit ? FireAct.WoodDrying : FireAct.WetWoodRefused;

                case ItemIds.FibreglassPanel:
                    if (Sheltered)
                    {
                        return FireAct.PanelPointless;
                    }

                    PanelPlaced = true;
                    return FireAct.PanelPlaced;

                case ItemIds.Kelp:
                    return FireAct.KelpRefused;

                case ItemIds.ChertNodule:
                    return Strike(hasMultitool);

                default:
                    return FireAct.Nothing;
            }
        }

        private FireAct Strike(bool hasMultitool)
        {
            if (!hasMultitool)
            {
                return FireAct.NoSpine;
            }

            if (IsLit)
            {
                return FireAct.AlreadyLit;
            }

            switch (Tinder)
            {
                case Tinder.None:
                    return FireAct.SparksOnSand;

                case Tinder.Grass:
                    // Burn the grass, watch it go out, no penalty, no scolding.
                    Tinder = Tinder.None;
                    return FireAct.GrassFlared;

                default:
                    if (!HasWood)
                    {
                        // The fibre stays: it will take again. Failing costs nothing (ADR-0019).
                        return FireAct.EmberStarved;
                    }

                    if (!Sheltered)
                    {
                        BlowOuts++;
                        return FireAct.BlewOut;
                    }

                    Tinder = Tinder.None;
                    IsLit = true;
                    return FireAct.Lit;
            }
        }

        /// <summary>Takes this site's kit for carrying: returns what was laid and clears it.</summary>
        public void TakeKit(out Tinder tinder, out bool wood)
        {
            tinder = Tinder;
            wood = HasWood;
            Tinder = Tinder.None;
            HasWood = false;
        }

        /// <summary>Lays a carried kit here, keeping whatever was better already.</summary>
        public void ReceiveKit(Tinder tinder, bool wood)
        {
            if (tinder > Tinder)
            {
                Tinder = tinder;
            }

            HasWood = HasWood || wood;
        }

        /// <summary>Back to bare sand.</summary>
        public void Reset()
        {
            PanelPlaced = false;
            Tinder = Tinder.None;
            HasWood = false;
            IsLit = false;
            BlowOuts = 0;
        }

        /// <summary>Packs the state: bit 1 panel, 2 wood, 4 lit, bits 3-4 tinder, the rest blow-outs.</summary>
        public int Capture()
        {
            return (PanelPlaced ? 1 : 0) | (HasWood ? 2 : 0) | (IsLit ? 4 : 0) | ((int)Tinder << 3) | (BlowOuts << 5);
        }

        /// <summary>Restores from <see cref="Capture"/>.</summary>
        public void Restore(int packed)
        {
            PanelPlaced = (packed & 1) != 0;
            HasWood = (packed & 2) != 0;
            IsLit = (packed & 4) != 0;
            var tinder = (packed >> 3) & 3;
            Tinder = tinder > (int)Tinder.Fibre ? Tinder.None : (Tinder)tinder;
            BlowOuts = packed >> 5;
        }
    }

    /// <summary>What the acts mean to the rest of the game.</summary>
    public static class FireRules
    {
        /// <summary>Seconds of play wet wood takes to dry in the warm zone (§4.2: "after 90 s").</summary>
        public const double WoodDryingSeconds = 90d;

        /// <summary>Blow-outs before Nadia says it is the wind (§7:10 FAILURE, tier 1).</summary>
        public const int BlowOutsBeforeTheWindLine = 3;

        /// <summary>Blow-outs before she carries the kit to the lee herself (tier 3).</summary>
        public const int BlowOutsBeforeSheCarriesIt = 7;

        /// <summary>True when the act took the item out of the player's hands.</summary>
        public static bool Consumes(FireAct act)
        {
            switch (act)
            {
                case FireAct.GrassLaid:
                case FireAct.FibreLaid:
                case FireAct.WoodStacked:
                case FireAct.PanelPlaced:
                case FireAct.WoodDrying:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the act was a refusal: the item stayed and nothing changed.</summary>
        public static bool IsRefusal(FireAct act)
        {
            switch (act)
            {
                case FireAct.PanelPointless:
                case FireAct.GrassPointless:
                case FireAct.AlreadyLit:
                case FireAct.WetWoodRefused:
                case FireAct.KelpRefused:
                case FireAct.NoSpine:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when sparks were thrown, whatever they landed in.</summary>
        public static bool ThrewSparks(FireAct act)
        {
            switch (act)
            {
                case FireAct.SparksOnSand:
                case FireAct.GrassFlared:
                case FireAct.EmberStarved:
                case FireAct.BlewOut:
                case FireAct.Lit:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The narration key for an act, or null for <see cref="FireAct.Nothing"/>.</summary>
        public static string NarrationKey(FireAct act)
        {
            switch (act)
            {
                case FireAct.GrassLaid: return "narration.fire.grass_laid";
                case FireAct.FibreLaid: return "narration.fire.fibre_laid";
                case FireAct.WoodStacked: return "narration.fire.wood_stacked";
                case FireAct.PanelPlaced: return "narration.fire.panel_placed";
                case FireAct.PanelPointless: return "narration.fire.panel_pointless";
                case FireAct.AlreadyLit: return "narration.fire.already_lit";
                case FireAct.WetWoodRefused: return "narration.fire.wet_wood";
                case FireAct.WoodDrying: return "narration.fire.wood_drying";
                case FireAct.KelpRefused: return "narration.fire.kelp";
                case FireAct.NoSpine: return "narration.fire.no_spine";
                case FireAct.SparksOnSand: return "narration.fire.sparks_on_sand";
                case FireAct.GrassFlared: return "narration.fire.grass_flared";
                case FireAct.EmberStarved: return "narration.fire.ember_starved";
                case FireAct.BlewOut: return "narration.fire.blew_out";
                case FireAct.Lit: return "narration.fire.lit";
                case FireAct.GrassPointless: return "narration.fire.grass_pointless";
                default: return null;
            }
        }
    }
}
