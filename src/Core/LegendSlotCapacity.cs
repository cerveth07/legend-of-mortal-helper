using System;
using System.Collections.Generic;

namespace MortalHelper.Core
{
    internal static class LegendSlotCapacity
    {
        internal const int Original = 12;
        internal const int Step = 6;

        internal static int Visible(int limit, IEnumerable<int> savedSlots)
        {
            var occupied = new HashSet<int>();
            int highest = 0;
            foreach (int slot in savedSlots)
                if (slot >= 1 && slot <= 999) { occupied.Add(slot); highest = Math.Max(highest, slot); }
            limit = Math.Max(highest, Math.Max(Original, Math.Min(999, limit)));
            int visible = Original;
            while (visible < limit)
            {
                bool full = true;
                for (int slot = 1; slot <= visible; slot++)
                    if (!occupied.Contains(slot)) { full = false; break; }
                if (!full && highest <= visible) break;
                visible = Math.Min(limit, visible + Step);
            }
            return visible;
        }
    }
}
