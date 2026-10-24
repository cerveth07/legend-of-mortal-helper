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
        private static EventSystem lockedSystem;

        internal static bool Held => holds != InteractionHold.None;

        internal static void Set(InteractionHold owner, bool active)
        {
            InteractionHold next = active ? holds | owner : holds & ~owner;
            if (next == holds) return;
            holds = next;
            if (holds == InteractionHold.None) { Release(); return; }
            Refresh();
        }

        // Re-asserts the lock while it is held. A scene that finishes loading
        // during a replay creates a fresh EventSystem whose input modules start
        // enabled; without this the player could click through a restore that is
        // still in progress. It also re-disables a module the game re-enabled.
        internal static void Refresh()
        {
            if (holds == InteractionHold.None) return;
            var system = EventSystem.current;
            if (system == null) return;
            if (!ReferenceEquals(system, lockedSystem))
            {
                // The old system may survive the switch (DontDestroyOnLoad, additive load).
                // Re-enable what we disabled there before forgetting it, or that system's input
                // stays dead for the rest of the process.
                foreach (var previous in Disabled) if (previous != null) previous.enabled = true;
                Disabled.Clear();
                lockedSystem = system;
            }
            foreach (var module in system.GetComponents<BaseInputModule>())
                if (module.enabled) { if (!Disabled.Contains(module)) Disabled.Add(module); module.enabled = false; }
        }

        private static void Release()
        {
            foreach (var module in Disabled) if (module != null) module.enabled = true;
            Disabled.Clear();
            lockedSystem = null;
        }

        // Dropping the lock state without holding a scene: used when the plugin is
        // torn down or the game returns to the title.
        internal static void Reset()
        {
            holds = InteractionHold.None;
            Release();
        }
    }
}
