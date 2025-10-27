using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eco.Gameplay.Components;
using Eco.Gameplay.Components.Auth;
using Eco.Gameplay.Components.Storage;
using Eco.Gameplay.Objects;
using Eco.Gameplay.Property;

namespace DeedStorage
{
    internal static class DeedStorageRegistry
    {
        private static readonly ConcurrentDictionary<int, ConcurrentDictionary<LinkComponent, byte>> LinksByDeed = new();
        private static readonly ConcurrentDictionary<LinkComponent, int> LinkToDeed = new();
        private static readonly ConcurrentDictionary<LinkComponent, Subscription> Subscriptions = new();

        internal static void Rebuild()
        {
            Clear();
            WorldObjectManager.ForEach(TryRegister);
        }

        internal static void Clear()
        {
            foreach (var subscription in Subscriptions.Values)
                subscription.Detach();

            Subscriptions.Clear();
            LinksByDeed.Clear();
            LinkToDeed.Clear();
        }

        internal static void TryRegister(WorldObject? obj)
        {
            if (obj == null) return;

            foreach (var link in obj.GetComponents<LinkComponent>())
            {
                Register(link);
            }
        }

        internal static void TryUnregister(WorldObject? obj)
        {
            if (obj == null) return;

            var links = obj.GetComponents<LinkComponent>().ToList();
            if (links.Count == 0)
                links = LinkToDeed.Keys.Where(link => link?.Parent == obj).ToList();

            foreach (var link in links)
            {
                Unregister(link, detach: true);
            }
        }

        internal static void Refresh(LinkComponent link) => Register(link);

        private static void Register(LinkComponent? link)
        {
            if (link?.Parent == null || link.Parent.IsDestroyed) return;

            if (IsVehicle(link.Parent) || link is MovableLinkComponent)
            {
                Unregister(link, detach: true);
                return;
            }

            Subscriptions.GetOrAdd(link, static key =>
            {
                var sub = new Subscription(key);
                sub.Attach();
                return sub;
            });

            if (!TryResolveDeedId(link, out var deedId))
            {
                Unregister(link, detach: false);
                return;
            }

            if (LinkToDeed.TryGetValue(link, out var existingId) && existingId == deedId)
            {
                EnsureClaimLinks(link, deedId);
                return;
            }

            if (existingId != 0 && existingId != deedId)
                RemoveFromBucket(link, existingId);

            LinkToDeed[link] = deedId;

            CleanupLinkedObjects(link);

            var bucket = LinksByDeed.GetOrAdd(deedId, _ => new ConcurrentDictionary<LinkComponent, byte>());
            foreach (var peer in bucket.Keys.ToArray())
            {
                if (peer == null)
                    continue;

                if (peer == link)
                    continue;

                if (peer.Parent == null || peer.Parent.IsDestroyed)
                {
                    bucket.TryRemove(peer, out _);
                    continue;
                }

                var linkIsLinkable = IsLinkable(link.Parent);
                var peerIsLinkable = IsLinkable(peer.Parent);

                if (linkIsLinkable && peerIsLinkable)
                {
                    DeedStorageLinker.TryLink(link, peer);
                }
            }

            bucket[link] = 0;
        }

        private static void CleanupLinkedObjects(LinkComponent link)
        {
            try
            {
                var linkedObjectsProp = typeof(LinkComponent).GetProperty("LinkedObjects", BindingFlags.Instance | BindingFlags.Public);
                if (linkedObjectsProp?.GetValue(link) is not System.Collections.IEnumerable linkedObjects)
                    return;

                var snapshot = new List<LinkComponent>();
                foreach (var item in linkedObjects)
                {
                    if (item is LinkComponent peer)
                        snapshot.Add(peer);
                }

                if (snapshot.Count == 0)
                    return;

                LinkToDeed.TryGetValue(link, out var linkDeedId);

                foreach (var peer in snapshot)
                {
                    if (peer == null)
                        continue;

                    if (peer.Parent == null || peer.Parent.IsDestroyed || !HasStorage(peer.Parent))
                    {
                        DeedStorageLinker.TryUnlink(link, peer);
                        continue;
                    }

                    if (linkDeedId != 0 && LinkToDeed.TryGetValue(peer, out var peerDeedId) && peerDeedId != 0 && peerDeedId != linkDeedId)
                    {
                        DeedStorageLinker.TryUnlink(link, peer);
                    }
                }
            }
            catch
            {
                // Best-effort cleanup; ignore errors
            }
        }

        private static void Unregister(LinkComponent? link, bool detach)
        {
            if (link == null) return;

            if (LinkToDeed.TryRemove(link, out var deedId) && deedId != 0)
            {
                if (LinksByDeed.TryGetValue(deedId, out var bucket))
                {
                    if (bucket.TryRemove(link, out _))
                    {
                        foreach (var peer in bucket.Keys.ToArray())
                        {
                            if (peer == null)
                                continue;

                            DeedStorageLinker.TryUnlink(link, peer);
                            Refresh(peer);
                        }
                    }

                    if (bucket.IsEmpty)
                        LinksByDeed.TryRemove(deedId, out _);
                }
            }

            if (detach && Subscriptions.TryRemove(link, out var subscription))
                subscription.Detach();
        }

        private static bool TryResolveDeedId(LinkComponent link, out int deedId)
        {
            deedId = 0;
            var parent = link.Parent;
            if (parent == null || parent.IsDestroyed) return false;

            Deed deed = parent.TryGetComponent<StandaloneAuthComponent>(out var standalone) ? standalone.Deed : parent.GetDeed();
            deed ??= PropertyManager.GetDeedWorldPos(parent.Position3i.XZ);

            if (deed == null) return false;

            deedId = deed.Id;
            return deedId != 0;
        }

        private static void RemoveFromBucket(LinkComponent link, int deedId)
        {
            if (deedId == 0) return;

            if (!LinksByDeed.TryGetValue(deedId, out var bucket))
                return;

            if (bucket.TryRemove(link, out _))
            {
                foreach (var peer in bucket.Keys.ToArray())
                {
                    if (peer == null)
                        continue;

                    DeedStorageLinker.TryUnlink(link, peer);
                    Refresh(peer);
                }
            }

            if (bucket.IsEmpty)
                LinksByDeed.TryRemove(deedId, out _);
        }

        private static void EnsureClaimLinks(LinkComponent link, int deedId)
        {
            if (!LinksByDeed.TryGetValue(deedId, out var bucket))
                return;

            foreach (var peer in bucket.Keys.ToArray())
            {
                if (peer == null)
                    continue;

                if (peer == link)
                    continue;

                if (peer.Parent == null || peer.Parent.IsDestroyed)
                {
                    bucket.TryRemove(peer, out _);
                    continue;
                }

                var linkIsLinkable = IsLinkable(link.Parent);
                var peerIsLinkable = IsLinkable(peer.Parent);

                if (linkIsLinkable && peerIsLinkable)
                {
                    DeedStorageLinker.TryLink(link, peer);
                }
            }
        }

        private static bool IsLinkable(WorldObject? obj)
        {
            if (IsVehicle(obj))
                return false;

            return HasStorage(obj) || HasCrafting(obj) || HasTrading(obj);
        }

        private static bool HasStorage(WorldObject? obj) => obj is { IsDestroyed: false } && obj.GetComponent<StorageComponent>() != null;
        private static bool HasCrafting(WorldObject? obj) => obj is { IsDestroyed: false } && obj.GetComponent<CraftingComponent>() != null;

        private static bool IsVehicle(WorldObject? obj)
        {
            if (obj is not { IsDestroyed: false })
                return false;

            try
            {
                if (obj.TryGetComponent<MovableLinkComponent>(out _))
                    return true;

                if (obj.TryGetComponent<VehicleComponent>(out _))
                    return true;

                foreach (var component in obj.GetComponents<WorldObjectComponent>())
                {
                    if (component == null)
                        continue;

                    var type = component.GetType();
                    if (type?.Name != null && type.Name.Contains("Vehicle", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (type?.FullName != null && type.FullName.Contains(".Vehicle", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                // best-effort
            }

            return false;
        }

        private static bool HasTrading(WorldObject? obj)
        {
            if (obj is not { IsDestroyed: false })
                return false;

            try
            {
                foreach (var component in obj.GetComponents<WorldObjectComponent>())
                {
                    if (component == null)
                        continue;

                    var type = component.GetType();
                    var name = type?.Name;
                    if (name == null)
                        continue;

                    if (name is "ShopComponent" or "StoreComponent" or "StoreSellComponent" or "StoreBuyComponent")
                        return true;

                    var fullName = type?.FullName;
                    if (fullName is not null && fullName.Contains(".ShopComponent", StringComparison.Ordinal))
                        return true;
                }
            }
            catch
            {
                // best-effort
            }

            return false;
        }

        private sealed class Subscription
        {
            private readonly LinkComponent link;
            private readonly Action<StorageComponent> syncHandler;
            private readonly Action<AuthComponent> authHandler;

            public Subscription(LinkComponent link)
            {
                this.link = link;
                this.syncHandler = _ => Refresh(link);
                this.authHandler = _ => Refresh(link);
            }

            public void Attach()
            {
                this.link.OnLinked.AddUnique(this.syncHandler);
                this.link.OnDelinked.AddUnique(this.syncHandler);
                this.link.Parent?.Auth?.AuthChanged.AddUnique(this.authHandler);
            }

            public void Detach()
            {
                this.link.OnLinked.Remove(this.syncHandler);
                this.link.OnDelinked.Remove(this.syncHandler);
                this.link.Parent?.Auth?.AuthChanged.Remove(this.authHandler);
            }
        }
    }
}
