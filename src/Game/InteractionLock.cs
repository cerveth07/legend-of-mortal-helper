using System;
using System.Collections.Generic;
using UnityEngine.EventSystems;

namespace MortalHelper.Game
{
    // Story recovery and duel recovery both suspend player input, and either can
    // finish first. Two independent "remember what I disabled" lists on the same
    // EventSystem disagree: the second owner never records a module the first one
    // already disabled, so releasing the first lock re-enables input while the
    // second recovery is still running. This keeps one list and one owner set.
    [Flags]
    internal enum InteractionHold { None = 0, Story = 1, Duel = 2 }

    internal static class InteractionLock
    {
        private static readonly List<BaseInputModule> Disabled = new List<BaseInputModule>();
        private static InteractionHold holds;

        internal static bool Held => holds != InteractionHold.None;

        internal static void Set(InteractionHold owner, bool active)
        {
            InteractionHold next = active ? holds | owner : holds & ~owner;
            if (next == holds) return;
            holds = next;
            var system = EventSystem.current;
            if (system == null) return;
            if (holds != InteractionHold.None)
            {
                foreach (var module in system.GetComponents<BaseInputModule>())
                    if (module.enabled) { if (!Disabled.Contains(module)) Disabled.Add(module); module.enabled = false; }
                return;
            }
            foreach (var module in Disabled) if (module != null) module.enabled = true;
            Disabled.Clear();
        }

        // Losing the EventSystem while holding the lock would otherwise leave the
        // stale list behind for the next scene.
        internal static void Reset()
        {
            foreach (var module in Disabled) if (module != null) module.enabled = true;
            Disabled.Clear();
            holds = InteractionHold.None;
        }
    }
}
