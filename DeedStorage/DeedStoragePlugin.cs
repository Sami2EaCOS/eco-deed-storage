using System;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Eco.Shared.Logging;

namespace DeedStorage
{
    public sealed class DeedStoragePlugin : IModKitPlugin, IInitializablePlugin
    {
        private static readonly LocString LogPrefix = new("[DeedStorage] ");

        private static readonly Action<WorldObject, User> AddedHandler = OnWorldObjectAdded;
        private static readonly Action<WorldObject> RemovedHandler = OnWorldObjectRemoved;

        public string GetCategory() => "Storage";

        public string GetStatus() => "Claim-wide storage links active";

        public void Initialize(TimedTask timer)
        {
            WorldObjectManager.Init.RunIfOrWhenInitialized(() =>
            {
                DeedStorageRegistry.Rebuild();
                Log.WriteLine(LogPrefix + "Initialized and registry seeded.");
            });

            WorldObjectManager.WorldObjectAddedEvent.Add(AddedHandler);
            WorldObjectManager.WorldObjectRemovedEvent.Add(RemovedHandler);
        }

        public void Shutdown()
        {
            WorldObjectManager.WorldObjectAddedEvent.Remove(AddedHandler);
            WorldObjectManager.WorldObjectRemovedEvent.Remove(RemovedHandler);

            DeedStorageRegistry.Clear();

            Log.WriteLine(LogPrefix + "Shutdown completed.");
        }

        private static void OnWorldObjectAdded(WorldObject obj, User _)
        {
            DeedStorageRegistry.TryRegister(obj);
        }

        private static void OnWorldObjectRemoved(WorldObject obj)
        {
            DeedStorageRegistry.TryUnregister(obj);
        }
    }
}
