"""Guard Unity UI mutation ordering where no headless game runtime exists."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def source(name):
    return (ROOT / "src/Game" / name).read_text(encoding="utf-8")


class UiMutationWiringTests(unittest.TestCase):
    def test_apply_confirmation_resets_with_script_and_row(self):
        hint = source("HintAdapter.cs")
        load = hint.split("internal static void Load(", 1)[1].split("internal static void Clear()", 1)[0]
        clear = hint.split("internal static void Clear()", 1)[1].split("private static bool HasContent", 1)[0]
        enter = hint.split("internal static void Enter(", 1)[1].split("internal static void Leave(", 1)[0]
        self.assertIn("armedApply = null;", load)
        self.assertIn("armedApply = null;", clear)
        self.assertIn("if (hovered != pointer) armedApply = null;", enter)

    def test_retry_checks_conflicts_and_unpatches_failed_attempt(self):
        plugin = source("Plugin.cs")
        retry = plugin.split("internal bool RepairPatches()", 1)[1].split("private void CheckLegacyPlugins()", 1)[0]
        self.assertLess(retry.index("CheckLegacyPlugins();"), retry.index("patches.PatchAll("))
        self.assertIn("patches?.UnpatchSelf();", retry)
        self.assertIn("patches = null;", retry)
        self.assertIn("SceneManager.activeSceneChanged -= SceneChanged;", retry)

    def test_refresh_failure_keeps_numeric_edit_success_and_undo(self):
        edits = source("NumericEdits.cs")
        change = edits.split("private static bool Change(", 1)[1].split("internal static void ApplyRecorded(", 1)[0]
        self.assertLess(change.index("write(value);"), change.index("NumericShopRefresh.Refresh(item);"))
        self.assertIn("catch (Exception error)", change.split("NumericShopRefresh.Refresh(item);", 1)[1])
        self.assertIn("return false;", change.split("NumericShopRefresh.Refresh(item);", 1)[1])
        panel = source("MainPanel.cs")
        item = panel.split("bool refreshed = NumericEdits.SetItem(", 1)[1].split("GUILayout.EndHorizontal();", 1)[0]
        self.assertLess(item.index("Record("), item.index("plugin.Status ="))
        self.assertIn("refreshed ?", item)


if __name__ == "__main__":
    unittest.main()
