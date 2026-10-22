"""Source-level guard checks, not a substitute for Unity game round trips."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


class SafetyWiringTests(unittest.TestCase):
    def test_unmarked_native_combat_load_has_no_required_companion(self):
        duel = read("src/Game/AnytimeDuel.cs")
        load = duel.split("internal static bool PrepareLoad(", 1)[1].split(
            "internal static void AfterNativeLoadScene()", 1)[0]
        self.assertIn('if (!File.Exists(path + ".mhduel"))', load)
        self.assertIn('if (File.Exists(path + ".mhduel-required"))', load)
        self.assertIn("return false;", load)
        self.assertLess(load.index('if (!File.Exists(path + ".mhduel"))'),
                        load.index('var loaded = Decode(path + ".mhduel");'))

    def test_duel_commit_marks_replay_dependency_before_clearing_pending(self):
        duel = read("src/Game/AnytimeDuel.cs")
        pair = duel.split("private static void WritePair(", 1)[1].split(
            "private static void Restore(", 1)[0]
        self.assertIn('string required = path + ".mhduel-required";', pair)
        self.assertLess(pair.index('Stage(required, Encoding.ASCII.GetBytes("1"));'),
                        pair.index('File.Delete(marker);'))
        self.assertIn('if (!hadRequired && File.Exists(required)) File.Delete(required);', pair)
        self.assertIn("if (rollback != null)", pair)

    def test_pending_transactions_prohibit_another_save(self):
        story = read("src/Game/AnytimeStory.cs")
        duel = read("src/Game/AnytimeDuel.cs")
        self.assertIn('File.Exists(path + ".mhduel-pending")', story)
        self.assertIn('File.Exists(path + ".mhduel-required") && !File.Exists(path + ".mhduel")', story)
        self.assertIn('File.Exists(path + ".mhduel-pending")', duel)

    def test_verified_native_overwrite_clears_stale_duel_metadata(self):
        native = read("src/Game/AnytimeNative.cs")
        self.assertIn("AnytimeStory.NativeSaveSucceeded(path, data)", native)
        self.assertIn("AnytimeStory.ForgetAfterOrdinarySave(path, data)", native)
        self.assertEqual(native.count('DeleteIfExists(path + ".mhduel-required");'), 2)

    def test_manual_speed_change_is_gated(self):
        plugin = read("src/Game/Plugin.cs")
        method = plugin.split("internal void ApplySpeed(", 1)[1].split(
            "private void SceneChanged(", 1)[0]
        self.assertIn("if (!Gate.CanInteract)", method)
        self.assertLess(method.index("if (!Gate.CanInteract)"),
                        method.index("Options.Speed.Value = value;"))
        self.assertIn("GUI.enabled = wasEnabled && plugin.Gate.CanInteract;",
                      read("src/Game/MainPanel.cs"))

    def test_recording_rejects_baseline_replay_would_refuse(self):
        recovery = read("src/Game/StoryRecovery.cs")
        begin = recovery.split("internal static bool Begin()", 1)[1].split(
            "internal static void Stop()", 1)[0]
        self.assertIn("native.TimeTick <= 0", begin)
        self.assertIn("string.IsNullOrEmpty(native.CurrentStoryScript)", begin)

    def test_imgui_editors_block_global_keyboard_shortcuts(self):
        panel = read("src/Game/MainPanel.cs")
        access = read("src/Game/GameAccess.cs")
        self.assertIn("EditingTextField = false;", panel)
        self.assertIn("GUI.GetNameOfFocusedControl() == name", panel)
        self.assertIn("Plugin.Instance?.Panel?.EditingTextField == true", access)
        self.assertEqual(panel.count("GUILayout.TextField("), 1)
        self.assertEqual(panel.count("EditableTextField("), 8)

    def test_lua_random_and_unsupported_table_checks_remain(self):
        story = read("src/Game/AnytimeStory.cs")
        self.assertIn("CaptureLuaRandom(lua)", story)
        self.assertIn("RestoreLuaRandom(lua, playback.LuaRandomState)", story)
        self.assertIn("!StandardLuaTables.Contains(pair.Key.String)", story)


if __name__ == "__main__":
    unittest.main()
