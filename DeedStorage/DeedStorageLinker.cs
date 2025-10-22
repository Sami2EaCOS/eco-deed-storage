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

        internal static void TryUnlink(LinkComponent first, LinkComponent second)
        {
            if (DelinkMethod == null) return;
            if (first == null || second == null || ReferenceEquals(first, second)) return;

            try
            {
                DelinkMethod.Invoke(first, new object[] { new[] { second } });
            }
            catch
            {
                // ignored
            }
        }
    }
}
