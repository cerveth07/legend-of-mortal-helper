"""Clear notes after durable save commit, never during save preparation/rollback."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class CommentSaveWiringTests(unittest.TestCase):
    def test_ordinary_cleanup_follows_committed_companion_retirement(self):
        source = (ROOT / 'src/Game/AnytimeNative.cs').read_text(encoding='utf-8')
        saved = source.split('internal static void SavedData(', 1)[1].split('internal static void EndSave(', 1)[0]
        committed, rollback = saved.split('catch (Exception error)', 1)
        self.assertLess(committed.index('FinishOperation(transaction, path);'), committed.index('SaveSlotComments.Saved(savingSlot);'))
        self.assertIn('SaveSlotComments.NativeSaved(savingSlot, path, data);', committed)
        self.assertNotIn('SaveSlotComments.', rollback)
        prepare = source.split('internal static bool BeforeSave(', 1)[1].split('internal static void SavedData(', 1)[0]
        self.assertNotIn('SaveSlotComments.Saved(', prepare)

    def test_story_cleanup_follows_pair_commit_and_precedes_context_reset(self):
        source = (ROOT / 'src/Game/AnytimeStory.cs').read_text(encoding='utf-8')
        saved = source.split('internal static void AfterNativeSave(', 1)[1].split('internal static void AbortNativeSave()', 1)[0]
        committed, rollback = saved.split('catch (Exception error)', 1)
        self.assertLess(committed.index('AnytimeNative.FinishOperation('), committed.index('SaveSlotComments.Saved(pendingSlot);'))
        self.assertLess(committed.index('SaveSlotComments.Saved(pendingSlot);'), committed.index('ClearPending();'))
        self.assertNotIn('SaveSlotComments.', rollback)

    def test_duel_cleanup_follows_verified_battle_start_pair(self):
        source = (ROOT / 'src/Game/AnytimeDuel.cs').read_text(encoding='utf-8')
        saved = source.split('internal static bool SaveToSlot(', 1)[1].split('internal static bool PrepareLoad(', 1)[0]
        self.assertLess(saved.index('WriteStartPair(path, native, startMarker);'), saved.index('SaveSlotComments.Saved(slot);'))
        self.assertNotIn('SaveSlotComments.', saved.split('catch (Exception e)\n', 1)[-1])


if __name__ == '__main__':
    unittest.main()
