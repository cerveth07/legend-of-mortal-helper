using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mortal.Core;

namespace MortalHelper.Game
{
    // Replay must recreate in-memory gameplay, but never repeat durable side effects.
    // Hook the final I/O boundary too: Lua autosaves do not always use SaveGameData.
    [HarmonyPatch]
    internal static class CheckpointWriteGuard
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "ExecuteSaveData", "ExecuteSaveJsonData", "DeleteSaveData", "SetAchivement", "DisplayAchivement" })
            {
                var method = AccessTools.Method(typeof(SaveSystem), name);
                if (method == null) throw new MissingMethodException(typeof(SaveSystem).FullName, name);
                yield return method;
            }
        }
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !AnytimeStory.Replaying && !AnytimeStory.Failed && !AnytimeDuel.Replaying;
    }
}
