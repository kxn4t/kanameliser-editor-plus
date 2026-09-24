using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kanameliser.EditorPlus.ComponentCopier
{
    /// <summary>
    /// The RequireComponent links between the components of one GameObject. Unity refuses to remove a component
    /// that another one requires, so these decide what can be removed, and in which order. Conservative: a
    /// component counts as required as soon as another one names its type, even where a second component of
    /// that type would satisfy the requirement.
    /// </summary>
    internal static class ComponentDependencies
    {
        /// <summary>True when another component on the same GameObject requires <paramref name="component"/>.</summary>
        public static bool IsRequiredByAnother(Component component) => FindRequirer(component, null) != null;

        /// <summary>
        /// Another component on the same GameObject that requires <paramref name="component"/>, or null.
        /// The components in <paramref name="removed"/> do not count: they are going to be removed as well.
        /// </summary>
        public static Component FindRequirer(Component component, ICollection<Component> removed)
        {
            var type = component.GetType();
            foreach (var other in component.GetComponents<Component>())
            {
                // Missing scripts come back as null
                if (other == null || other == component) continue;
                if (removed != null && removed.Contains(other)) continue;
                if (Requires(other.GetType(), type)) return other;
            }

            return null;
        }

        /// <summary>
        /// The <paramref name="candidates"/> for removal, all on one GameObject, that have to stay, each with a
        /// component that requires it.
        /// </summary>
        public static Dictionary<Component, Component> FindKept(IEnumerable<Component> candidates) =>
            FindKept(candidates, FindRequirer);

        /// <summary>
        /// A candidate stays when something that is not removed requires it: a component that is no candidate, or
        /// a candidate that stays itself. Keeping one can therefore keep others, so this repeats until nothing
        /// changes. With C → B → A (C requires B, which requires A) and A and B as the candidates, B stays for C,
        /// and then A for B. <paramref name="findRequirer"/> is <see cref="FindRequirer"/>; it is passed in so
        /// that a chain can be tested without components that form one.
        /// </summary>
        internal static Dictionary<T, T> FindKept<T>(IEnumerable<T> candidates, Func<T, ICollection<T>, T> findRequirer)
            where T : class
        {
            var removed = new HashSet<T>(candidates);
            var kept = new Dictionary<T, T>();

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var candidate in removed.ToList())
                {
                    var requirer = findRequirer(candidate, removed);
                    if (requirer == null) continue;

                    removed.Remove(candidate);
                    kept[candidate] = requirer;
                    changed = true;
                }
            }

            return kept;
        }

        /// <summary>
        /// True when <paramref name="requirer"/> carries a RequireComponent that <paramref name="type"/> satisfies.
        /// </summary>
        private static bool Requires(Type requirer, Type type)
        {
            foreach (RequireComponent attribute in requirer.GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (Satisfies(type, attribute.m_Type0) || Satisfies(type, attribute.m_Type1) ||
                    Satisfies(type, attribute.m_Type2))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Satisfies(Type type, Type required) => required != null && required.IsAssignableFrom(type);
    }
}
