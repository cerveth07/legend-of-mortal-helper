"""Source-level guard checks, not a substitute for Unity game round trips.

These assert that the save slot plumbing keeps the shape the file-level fault
injection tests in tests/Contracts.cs rely on. They prove wiring only.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


def between(text, start, end):
    return text.split(start, 1)[1].split(end, 1)[0]


class SafetyWiringTests(unittest.TestCase):
    def test_unmarked_native_combat_load_has_no_required_companion(self):
        duel = read("src/Game/AnytimeDuel.cs")
        load = between(duel, "internal static bool PrepareLoad(", "internal static void AfterNativeLoadScene()")
        self.assertIn('if (!File.Exists(path + ".mhduel"))', load)
        self.assertIn('if (File.Exists(path + ".mhduel-required"))', load)
        self.assertIn("return false;", load)
        self.assertLess(load.index('if (!File.Exists(path + ".mhduel"))'),
                        load.index('var loaded = Decode(path + ".mhduel");'))

    def test_rollback_is_never_derived_from_backup_existence(self):
        story = read("src/Game/AnytimeStory.cs")
        rollback = between(story, "private static bool RollBack()", "private static void ClearPending()")
        # The old bug: "backup missing" was read as "the slot was empty" and the
        # live save was deleted. Rollback must delegate that decision entirely.
        self.assertNotIn("else DeleteIfExists(", rollback)
        self.assertNotIn("File.Delete(", rollback)
        self.assertIn("transaction.Rollback()", rollback)
        self.assertIn("pendingTransaction = SlotTransaction.Begin(", story)
        self.assertIn('"story"', story)

    def test_transaction_keeps_every_artefact_when_it_cannot_restore(self):
        transaction = read("src/Core/SlotTransaction.cs")
        rollback = between(transaction, "public SlotRollbackReport Rollback()", "private bool CanRestore(")
        # A blocker is detected before any target is written, and the journal is
        # only promoted to Broken so a later run keeps refusing.
        self.assertIn("if (!entry.Existed) continue;", rollback)
        self.assertIn("CanRestore(entry)", rollback)
        self.assertLess(rollback.index("if (report.Blockers.Count > 0)"),
                        rollback.index("// Phase 2: restore"))
        self.assertIn("SlotTransactionState.Broken", rollback)
        self.assertNotIn("File.Delete(JournalPath)", rollback.split("if (!complete)", 1)[0])

    def test_duel_promise_is_verified_before_it_is_declared_committed(self):
        duel = read("src/Game/AnytimeDuel.cs")
        pair = between(duel, "private static void WritePair(", "private static void DeleteIfExists(")
        self.assertIn('SlotTransaction.Begin(SlotTransaction.JournalFor(path), "duel"', pair)
        self.assertLess(pair.index("SlotTransaction.Begin("), pair.index("Stage(first, native)"))
        self.assertLess(pair.index("Decode(extra).NativeHash != Hash(native)"), pair.index("transaction.RecordReplacement("))
        self.assertLess(pair.index("transaction.RecordReplacement("), pair.index("transaction.Commit();"))
        self.assertLess(pair.index("transaction.Commit();"), pair.index("transaction.Complete();"))
        self.assertIn("transaction.Rollback()", pair)

    def test_ordinary_overwrite_journals_before_the_game_writes(self):
        native = read("src/Game/AnytimeNative.cs")
        supersede = between(native, "private static bool BeginSupersede(", "internal static void SavedData(")
        self.assertIn('"supersede"', supersede)
        self.assertIn("SettleSlot(slot, path)", supersede)
        saved = between(native, "internal static void SavedData(", "internal static void EndSave(")
        # The verified replacement is recorded before the old companions go away,
        # so a crash in between is finished forward instead of rejected later.
        self.assertLess(saved.index("transaction.RecordReplacement("), saved.index("FinishOperation("))
        self.assertIn("transaction.Rollback()", saved)

    def test_load_settles_the_journal_before_the_game_reads_the_slot(self):
        native = read("src/Game/AnytimeNative.cs")
        begin = between(native, "internal static void BeginLoad(", "internal static void EndLoad(")
        self.assertIn("SettleSlot(loadingSlot, PathFor(loadingSlot))", begin)
        prepare = between(native, "internal static void PrepareLoad(ref GameSave data)", "internal static bool Numbered(")
        self.assertIn("PrepareLoad", prepare)
        self.assertLess(begin.index("SettleSlot("), begin.index("EndLoad") if "EndLoad" in begin else len(begin))

    def test_settled_journal_finishes_forward_only_for_a_verified_replacement(self):
        native = read("src/Game/AnytimeNative.cs")
        settle = between(native, "internal static void SettleSlot(", "internal static void PurgeOrphans(")
        self.assertIn("transaction.ReplacementHash", settle)
        self.assertIn("SlotTransaction.HashFile(path) == transaction.ReplacementHash", settle)
        self.assertLess(settle.index("if (forward)"), settle.index("transaction.Rollback()"))
        self.assertIn("SlotTransactionState.Broken", settle)

    def test_slot_delete_retires_the_companions(self):
        native = read("src/Game/AnytimeNative.cs")
        self.assertIn('[HarmonyPatch(typeof(SaveSystem), "DeleteSaveData")]', native)
        forget = between(native, "internal static void ForgetSlotFiles(", "internal static void BeginLoad(")
        self.assertIn("PurgeOrphans(path)", forget)
        self.assertIn("transaction.Complete()", forget)

    def test_stale_companion_never_rejects_a_complete_native_save(self):
        story = read("src/Game/AnytimeStory.cs")
        load = between(story, "internal static void PrepareLoad(string slot", "internal static void AfterNativeLoadScene()")
        self.assertIn("ReadSidecarHeader(path)", load)
        self.assertLess(load.index("if (recorded != null && recorded != nativeHash)"),
                        load.index("var record = ReadRecord(path, save);"))
        self.assertIn("Quarantine(path + \".mha\")", load)
        # A sidecar that is still marked required is a genuine ambiguity and must
        # keep refusing instead of silently downgrading to the native checkpoint.
        self.assertIn('if (File.Exists(path + ".mha-required"))', load)
        duel = read("src/Game/AnytimeDuel.cs")
        self.assertIn('Quarantine(path + ".mhduel")', duel)

    def test_pending_transactions_prohibit_another_save(self):
        story = read("src/Game/AnytimeStory.cs")
        duel = read("src/Game/AnytimeDuel.cs")
        native = read("src/Game/AnytimeNative.cs")
        self.assertIn('File.Exists(path + ".mhduel-pending")', story)
        self.assertIn('File.Exists(path + ".mhduel-required") && !File.Exists(path + ".mhduel")', story)
        self.assertIn('File.Exists(path + ".mhduel-pending")', duel)
        self.assertIn("SlotTransaction.Exists(SlotTransaction.JournalFor(path))", native)
        self.assertIn("if (File.Exists(JournalFor(path)))", story)

    def test_orphan_companions_are_purged_instead_of_blocking_saves(self):
        native = read("src/Game/AnytimeNative.cs")
        purge = between(native, "internal static void PurgeOrphans(", "internal static void ForgetSlotFiles(")
        self.assertIn("|| File.Exists(path)) return;", purge)
        for suffix in (".mha", ".mha-required", ".mhduel", ".mhduel-required"):
            self.assertIn(suffix, purge)

    def test_replay_keys_never_share_an_unknown_placeholder(self):
        recovery = read("src/Game/StoryRecovery.cs")
        # The placeholder may still be named in explanatory comments, but no code
        # path may substitute it for a missing script name.
        self.assertNotIn('? "unknown" :', recovery)
        self.assertNotIn('"unknown";', recovery)
        for builder in ("internal static string ChoiceKey(", "internal static string DiceKey("):
            body = between(recovery, builder, "}")
            self.assertIn("if (string.IsNullOrEmpty(scriptName))", body)
            self.assertIn("throw new InvalidDataException(", body)
        replay = read("src/Game/StoryReplay.cs")
        self.assertEqual(replay.count("if (!StoryRecovery.ScriptKnown)"), 2)

    def test_legacy_rollback_copies_are_never_deleted(self):
        native = read("src/Game/AnytimeNative.cs")
        purge = between(native, "internal static void PurgeOrphans(", "internal static void ForgetSlotFiles(")
        # With no .dat present these copies may be the only surviving save.
        self.assertNotIn('DeleteIfExists(path + ".mha-rollback")', purge)
        self.assertNotIn('DeleteIfExists(path + ".mha.rollback")', purge)
        story = read("src/Game/AnytimeStory.cs")
        self.assertIn("请勿手动删除", story)

    def test_manual_speed_change_is_gated(self):
        plugin = read("src/Game/Plugin.cs")
        method = between(plugin, "internal void ApplySpeed(", "private void SceneChanged(")
        self.assertIn("if (!Gate.CanInteract)", method)
        self.assertLess(method.index("if (!Gate.CanInteract)"),
                        method.index("Options.Speed.Value = value;"))
        self.assertIn("GUI.enabled = wasEnabled && plugin.Gate.CanInteract;",
                      read("src/Game/MainPanel.cs"))

    def test_recording_rejects_baseline_replay_would_refuse(self):
        recovery = read("src/Game/StoryRecovery.cs")
        begin = between(recovery, "internal static bool Begin()", "internal static void Stop()")
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
