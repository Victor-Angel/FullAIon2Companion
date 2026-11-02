"""Recipe id -> [result item id, result count] for the companion's crafting card (desktop/FullAIon2Companion/craft-recipes.json).

The game names the recipe of every craft (0x6F8D); the card shows what it makes with the item's name and icon from items.json.
Source: public/data/game/encyclopedia/recipes.json (the encyclopedia's crafting catalog).
"""
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
recipes = json.loads((root / 'public/data/game/encyclopedia/recipes.json').read_text(encoding='utf-8'))
out = {}
for r in recipes:
    result = r.get('result') or {}
    if isinstance(r.get('id'), int) and isinstance(result.get('id'), int):
        out[str(r['id'])] = [result['id'], int(result.get('count') or 1)]
target = root / 'desktop/FullAIon2Companion/craft-recipes.json'
target.write_text(json.dumps(out, separators=(',', ':'), sort_keys=True), encoding='utf-8')
print(len(out), 'recipes', target.stat().st_size, 'bytes')
