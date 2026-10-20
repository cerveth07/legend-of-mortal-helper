using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    // Development-only, read-only diagnostics. Enabling a config key must never
    // load another save, overwrite universe data, erase history or drive choices.
    internal static class Probe
    {
        private static bool logged;
        internal static void Tick()
        {
            var plugin = Plugin.Instance;
            if (plugin == null || !plugin.Options.SelfTest.Value) { logged = false; return; }
            if (logged) return;
            logged = true;
            try
            {
                plugin.Log("Probe (read-only): scene=" + SceneManager.GetActiveScene().name +
                    "; loaded=" + GameAccess.Loaded + "; canEdit=" + GameAccess.CanEditValues +
                    "; dice=" + (DiceAdapter.Dialog != null) + "; timeScale=" + Time.timeScale);
            }
            catch (Exception error) { plugin.Report(error); }
        }
    }
}
