using System.Collections.Generic;
using ForgottenIsle.Core.Commands;
using ForgottenIsle.Core.Localization;
using ForgottenIsle.Core.Logging;
using ForgottenIsle.Core.Save;
using ForgottenIsle.Core.Signals;
using ForgottenIsle.Core.Time;
using ForgottenIsle.Game.Diagnostics;
using ForgottenIsle.Game.Input;
using ForgottenIsle.Game.Localization;
using ForgottenIsle.Game.Interaction;
using ForgottenIsle.Game.Items;
using ForgottenIsle.Game.Progress;
using ForgottenIsle.Game.Saves;
using ForgottenIsle.Game.Scenes;
using ForgottenIsle.Game.Session;
using UnityEngine;

namespace ForgottenIsle.Game.Bootstrap
{
    /// <summary>
    /// Builds the entire application object graph, by hand, in one method (ADR-0012).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY hand-wired: Phase 1 constructs about a dozen objects. A container at that size buys nothing
    /// and costs a dependency, a learning curve, and a layer of indirection sitting between a reviewer
    /// and the answer to "what depends on what" — during the exact phase whose purpose is to make that
    /// answer obvious. Every line below is a dependency edge, written down, in order.
    /// </para>
    /// <para>
    /// WHY the order of the lines is not arbitrary: it is a topological sort of the graph. Anything
    /// constructed later may depend on anything constructed earlier and nothing may depend on
    /// something below it, which is what guarantees there is no cycle to break with a setter later.
    /// </para>
    /// </remarks>
    public static class AppCompositionRoot
    {
        /// <summary>
        /// Constructs every application-scoped service and registers the Phase 1 command handlers.
        /// </summary>
        /// <param name="host">
        /// The persistent bootstrap behaviour. It is the coroutine driver for <see cref="SceneLoader"/>
        /// and must outlive every scene, which is why it is passed in rather than created here — this
        /// method must not own a <c>GameObject</c>'s lifetime.
        /// </param>
        /// <returns>A fully wired context. Never null.</returns>
        public static GameContext Build(MonoBehaviour host)
        {
            var log = new UnityCoreLog();
            var signals = new SignalBus();
            var clock = new IslandClock();
            var localization = BuildLocalization(log);
            var states = new GameStateMachine(log, signals);
            var input = new InputRouter(states);
            var fileStore = new SaveFileStore(log);
            var codec = new SaveCodec();
            var slots = new SaveSlotService(fileStore, codec, log, Application.version);
            var session = new SessionService(clock, log, signals);
            var sceneLoader = new SceneLoader(host, log);
            var zones = new ZoneRegistry(sceneLoader, log, ZoneRegistry.DefaultMaxResident);
            var dispatcher = new CommandDispatcher(log);
            var progress = new ProgressService(signals, log);
            var inventory = new InventoryService(signals, log);
            var interactions = new InteractionSystem(progress, inventory, dispatcher, signals, log);

            // ADR-0011: every phase adds its participant in the same pull
            // request that adds its system. Registration order is capture and restore order, and the
            // session must precede the player: the player section is meaningless without the run that
            // gives its coordinates a zone to be in.
            // Phase 2 adds the third: progression. It is registered after the player because a
            // restored discovery only means anything once the run and its position exist.
            // Phase 3 adds the fourth: the inventory, last, because what the player carries is only
            // meaningful once the run, the position and the progression it was earned against exist.
            var participants = new List<ISaveParticipant>(4)
            {
                session, session.PlayerParticipant, progress, inventory
            };
            for (var i = 0; i < participants.Count; i++)
            {
                slots.RegisterParticipant(participants[i]);
            }

            // Resume is registered after the participant loop above, and that ordering is load-bearing
            // rather than cosmetic: ResumeSavedRunHandler restores a run by calling SaveSlotService.Load,
            // which walks the participant register. A handler wired before the register was filled would
            // resume into an empty world and report success.
            dispatcher.Register<StartNewGameCommand>(new StartNewGameHandler(states, session, zones, progress, log));
            dispatcher.Register<InspectCommand>(new InspectHandler(states, progress, signals));
            dispatcher.Register<CollectCommand>(new CollectHandler(states, progress, signals));
            dispatcher.Register<ResumeSavedRunCommand>(new ResumeSavedRunHandler(states, slots, session, zones, log));
            dispatcher.Register<SaveGameCommand>(new SaveGameHandler(slots, session, states, signals, log));
            dispatcher.Register<QuitToMenuCommand>(new QuitToMenuHandler(states, zones, session, log));
            dispatcher.Register<TravelToZoneCommand>(new TravelToZoneHandler(states, zones, session, sceneLoader, progress, log));
            dispatcher.Register<TakeItemCommand>(new TakeItemHandler(states, inventory, signals));
            dispatcher.Register<CombineItemsCommand>(new CombineItemsHandler(states, inventory, signals));
            dispatcher.Register<UseItemCommand>(new UseItemHandler(states, inventory, interactions, signals));

            return new GameContext(log, signals, clock, localization, states, dispatcher, session, sceneLoader, zones, slots, input, progress, interactions, inventory, participants);
        }

        /// <summary>
        /// Loads the shipped string tables and selects the starting locale.
        /// </summary>
        /// <remarks>
        /// Failure here is deliberately non-fatal. <see cref="StringTableLocalization"/> answers a
        /// missing key with <c>#key#</c> and never throws, so a game with no tables is ugly and
        /// completely playable — which is the right trade for a resource that can go missing in a
        /// stripped build long after the code that reads it was reviewed. The loader has already
        /// reported the absence as <see cref="LogCode.CatalogMissing"/> by the time it returns zero.
        /// </remarks>
        private static ILocalizedText BuildLocalization(ICoreLog log)
        {
            var localization = new StringTableLocalization(log, LocalizationLoader.AuthoringLocale);
            LocalizationLoader.LoadAll(localization, log);
            localization.SetLocale(LocalizationLoader.ResolveStartingLocale(localization));
            return localization;
        }
    }
}
