using System;
using System.Collections.Generic;
using System.Reflection;
using Eco.Gameplay.Components;

namespace DeedStorage
{
    internal static class DeedStorageLinker
    {
        private static readonly MethodInfo? DualLinkMethod =
            typeof(LinkComponent).GetMethod("DualLink", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo? DelinkMethod =
            typeof(LinkComponent).GetMethod(
                "Delink",
                BindingFlags.Instance | BindingFlags.NonPublic,
                Type.DefaultBinder,
                new[] { typeof(IEnumerable<LinkComponent>) },
                null);

        private static readonly PropertyInfo? LinkedObjectsProperty =
            typeof(LinkComponent).GetProperty("LinkedObjects", BindingFlags.Instance | BindingFlags.Public);

        private static readonly MethodInfo? LinkedObjectsRemoveMethod =
            LinkedObjectsProperty?.PropertyType.GetMethod("TryRemove", BindingFlags.Instance | BindingFlags.Public, Type.DefaultBinder, new[] { typeof(LinkComponent) }, null)
            ?? LinkedObjectsProperty?.PropertyType.GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public, Type.DefaultBinder, new[] { typeof(LinkComponent) }, null);

        internal static bool TryLink(LinkComponent first, LinkComponent second)
        {
            if (DualLinkMethod == null) return false;
            if (first == null || second == null || ReferenceEquals(first, second)) return false;

            try
            {
                return DualLinkMethod.Invoke(first, new object[] { second }) is bool result && result;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryUnlink(LinkComponent first, LinkComponent second)
        {
            if (first == null || second == null || ReferenceEquals(first, second))
                return false;

            var success = false;

            try
            {
                if (DelinkMethod != null)
                {
                    DelinkMethod.Invoke(first, new object[] { new[] { second } });
                    success = true;
                }
            }
            catch
            {
                // ignored, will attempt fallback below
            }

            if (!success)
            {
                try
                {
                    if (DelinkMethod != null)
                    {
                        DelinkMethod.Invoke(second, new object[] { new[] { first } });
                        success = true;
                    }
                }
                catch
                {
                    // ignored, will attempt final fallback below
                }
            }

            if (!success)
            {
                var removed = RemoveFromLinkedObjects(first, second) | RemoveFromLinkedObjects(second, first);
                if (removed)
                {
                    success = true;

                    try
                    {
                        foreach (var comp in first.Parent?.GetComponents<Eco.Gameplay.Components.Storage.StorageComponent>() ?? Array.Empty<Eco.Gameplay.Components.Storage.StorageComponent>())
                            second.OnDelinked.Invoke(comp);

                        foreach (var comp in second.Parent?.GetComponents<Eco.Gameplay.Components.Storage.StorageComponent>() ?? Array.Empty<Eco.Gameplay.Components.Storage.StorageComponent>())
                            first.OnDelinked.Invoke(comp);

                        first.OnInventoryContentsChanged.Invoke();
                        second.OnInventoryContentsChanged.Invoke();
                    }
                    catch
                    {
                        // best-effort
                    }
                }
            }

            return success;
        }

        private static bool RemoveFromLinkedObjects(LinkComponent owner, LinkComponent target)
        {
            try
            {
                if (owner == null || LinkedObjectsProperty == null)
                    return false;

                var collection = LinkedObjectsProperty.GetValue(owner);
                if (collection == null)
                    return false;

                if (LinkedObjectsRemoveMethod == null)
                    return false;

                return LinkedObjectsRemoveMethod.Invoke(collection, new object[] { target }) is bool result && result;
            }
            catch
            {
                return false;
            }
        }
    }
}
