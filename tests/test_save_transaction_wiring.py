"""Source wiring checks for save guards that require the live game assembly."""

from pathlib import Path
import unittest


NATIVE = (Path(__file__).resolve().parents[1] / "src/Game/AnytimeNative.cs")


class SaveTransactionWiringTests(unittest.TestCase):
    def test_old_companion_cleanup_blocks_journal_completion(self):
        code = NATIVE.read_text(encoding="utf-8")
        finish = code.split("internal static void FinishOperation(", 1)[1].split(
            "// Settles any journal", 1
        )[0]
        self.assertLess(finish.index("transaction.Commit()"), finish.index("RetireCompanions("))
        self.assertLess(finish.index("RetireCompanions(transaction.Operation, path)"),
                        finish.index("transaction.Complete()"))
        self.assertNotIn("TryDelete(", finish)
        self.assertIn('DeleteIfExists(path + ".mha-required")', finish)
        self.assertIn('DeleteIfExists(path + ".mhduel-required")', finish)

    def test_ordinary_save_rejects_legacy_pending_markers_before_write(self):
        code = NATIVE.read_text(encoding="utf-8")
        begin = code.split("private static bool BeginSupersede(", 1)[1].split(
            "internal static void SavedData(", 1
        )[0]
        self.assertIn('File.Exists(path + ".mha-pending")', begin)
        self.assertIn('File.Exists(path + ".mhduel-pending")', begin)
        self.assertLess(begin.index('File.Exists(path + ".mha-pending")'),
                        begin.index("QuarantineOrphans(path)"))
        self.assertLess(begin.index('File.Exists(path + ".mha-pending")'),
                        begin.index("if (!HasCompanion(path)) return true;"))


if __name__ == "__main__":
    unittest.main()
