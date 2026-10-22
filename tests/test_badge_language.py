"""Guard the fixed brand scroll text against the panel language toggle.

The collapsed badge is a vertical scroll that always reads 萬象錄 in traditional
characters. Routing it through the panel's simplified/traditional switch turned
it into 万象录, so the badge draw call must not use the localizing label helper.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
BADGE = 'GUI.Label(new Rect(badge.x + 2f * s, titleY, badge.width - 4f * s, titleH), "萬\\n象\\n錄", badgeTitleStyle);'


def read(path):
    return (ROOT / path).read_text(encoding="utf-8")


class BadgeLanguageTests(unittest.TestCase):
    def test_badge_scroll_is_drawn_with_traditional_characters(self):
        panel = read("src/Game/MainPanel.cs")
        self.assertIn(BADGE, panel)

    def test_badge_scroll_is_not_passed_through_the_language_toggle(self):
        panel = read("src/Game/MainPanel.cs")
        start = panel.index("private void DrawBadge()")
        body = panel[start:panel.index("private void", start + 20)]
        self.assertNotIn('UiLabel(new Rect(badge.x + 2f * s, titleY', body)
        self.assertNotIn('Localize("萬', body)

    def test_language_toggle_still_converts_panel_body_text(self):
        panel = read("src/Game/MainPanel.cs")
        self.assertIn("private string Localize(string text)", panel)
        self.assertIn("private void UiLabel(string text, GUIStyle design", panel)
        language = read("src/Game/UiLanguage.cs")
        self.assertIn("萬", language)  # table carries 萬 -> 万 in the simplified direction
        self.assertIn("万", language)


if __name__ == "__main__":
    unittest.main()
