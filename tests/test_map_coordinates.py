import json
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / 'src/Game/MapViewer.cs').read_text(encoding='utf-8')


def entries(name):
    match = re.search(r'\b' + name + r' = \{(.*?)\};', SOURCE, re.S)
    assert match, name
    return match.group(1)


def points(name):
    return [(float(x), float(y)) for x, y in
            re.findall(r'new Vector2\(([\d.]+)f?, ([\d.]+)f?\)', entries(name))]


class MapCoordinates(unittest.TestCase):
    def test_world_annotations_match_game_scene_records(self):
        labels = json.loads((ROOT / 'docs/map/labels.json').read_text(encoding='utf-8'))
        nodes = json.loads((ROOT / 'docs/map/nodes.json').read_text(encoding='utf-8'))
        self.assertEqual(re.findall(r'"([^"]+)"', entries('PlaceNames')),
                         [label['text'] for label in labels])
        self.assertEqual(points('PlacePoints'), [(label['x'], label['y']) for label in labels])
        self.assertEqual(points('NodePoints'), [(node['x'], node['y']) for node in nodes])
        self.assertNotIn('map-world.png', SOURCE)


if __name__ == '__main__':
    unittest.main()
