"""Builds desktop/FullAIon2Companion/atlas-npcs/<map>.json for the Atlas inside the app (ui/atlas.js): for each creature
category of the website's atlas (public/data/atlas/<map>.json), the creature (NPC id, or the first variant's) and its race
of every marker, in the same order as the app's markers (minimap-markers/<map>.json, checked marker by marker: 46,437 of
46,437 on 2026-10-17), and each creature's portrait (the icon of its catalog record, public/data/game/npc/<id>.json, when the
app ships it in assets/game or assets/pets; else the icon of the pet whose soul it drops, the one the Pets page shows; else
aion2.th.gl's; never a generic map marker). Races: C Cogni, F Fera, N Natura, V Varian, - unknown.
Output: {"categories": {id: {"npcs": [...], "races": "CF-..."}}, "icons": {npc: path under /assets/}}.
aion2.th.gl's portraits are used with its authors' permission (given to the owner on 2026-10-17), on condition that our
website credits them (footer and /endpoints/; the app's Atlas credits them too). --fetch downloads its creature list
(research/thgl/bestiary-es.json) and only the portraits the map lacks (desktop/FullAIon2Companion/thgl-icons), one at a
time; without it, only what is already here is used.
Creature places from the game's catalog (2026-11-08). The website's atlas (InteractiveMap.app) has creatures that are not where
the game puts them, or one marker for a whole spawn group: «Insectoide mineral gigante adulto» (2400473) had one marker some
100 px east of Yacimiento de Fafnite, while the catalog (and wikily.gg) has its 11 spawn points inside it; «Dratona peón» had 1
marker for 104 points. Where the two disagreed in our recordings (Altgard, 2026-10-11 to 11-04), the game sided with the
catalog 14 times and with the atlas twice. So the creature layers ("criaturas") now take each monster's spawn points from its
catalog record (public/data/game/npc/<id>.json, spawns.<map slug>.points), placed with a per-map fit from catalog units to
atlas pixels (Theil-Sen on the centroids of monsters with as many points as markers, then least squares on every marker
within 6 px of its own monster's point; median error 0.2-2.5 px). Every variant of a name counts as one group (the website
tags a marker with the first variant's id). Rules, refined over three independent audits with our recordings (2026-11-08/09):
- A catalog point with an old marker of its group within 3 px keeps that marker, which takes the point's monster id, race and
  catalog name (the website called all seven «Araña de rama seca», Nv. 11 to 40, by the Nv. 11 one; «Crasaur» -> Crasauro).
- Points of a group within 1.5 px are one place (a pack), and so is a point whose old marker is already taken.
- A spawn group's members share the very same points in the catalog (0.3 px at most); where the website gave such a spot to
  another member, standing on that member's own point, it stays that member's - unless that leaves the monster nowhere (the
  15 spots of «Cría de ratón salvaje quemado» are all its mother's).
- A monster the website did not tag or name takes the untagged markers standing within 2 px of its points (13 «Abyss Old
  Rotan» are Rotan oxidado del Abismo's).
- Any other point is added, in the layer where the monster's markers were (ties: the first seen), else its race's.
- An old marker no point took: goes if a marker of its own kept on that spot is within 1.5 px (a copy, in another layer);
  stays if the website draws it with another monster standing on its own point (a spot either spawns at: the Pretor scouts
  of the Eltnen quarry); goes if it sits alone on another monster's catalog point (the website swapped the earth and fire
  spirit stones); within 150 px of the monster's points, goes if it belongs to a camp the website drew moved (one shift,
  voted over the vectors from the loose markers to the points no kept marker stands on, takes 40 % of them there: the
  Brigada de Lagta, 68 px north; voting on every point, dense camps found a shift by chance), stays if 3 or more stand where
  the catalog has no point of any monster within 20 px (a camp it lacks: the Legion's compound), and goes otherwise (the
  website's copy, placed off; the lone «Insectoide mineral gigante adulto», 97 px east); farther, a lone label (fewer than 3
  within 80 px) within 400 px of a monster with 8 points or more goes, and the rest stays (36 «Espíritu de piedra del campo
  nevado» 612 px off in Morheim; the mirrored side of Reshanta Superior, which the catalog has for one side only).
- Every tagged creature marker says its monster's catalog name, as the cards do (the website's «Kailin» -> Kailín).
Kept as the atlas has them: monsters without catalog points, groups whose points are all over 300 px from their markers
(another layer of the map), monsters only on another layer (bosses), maps whose fit is not good, and the other layers (pets,
bosses, NPCs...). Then the places the game itself showed us (research/atlas/observed-spawns.tsv, the app's --spawn-dump of
recordings: map, npc, world x, y) for monsters the catalog has no points for: an untagged marker within 2 px takes the id
and name (and so does every untagged marker with that label), a marker of the name within 15 px tagged with a variant the
game never showed here takes this one's, else a marker is added; every sighting counts, so none sits over 15 px from one.
ATLAS_LOG=<file> writes each decision.
The website's markers as they came are research/atlas/markers-source/<map>.json (the app's form) and
research/atlas/web-source/<map>.json (the website's); this script writes the app's minimap-markers/<map>.json and the
website's public/data/atlas/<map>.json from them, with the same creature layers, so it can run again and gives the same
files (after changing these, bump src/lib/game-data-version.ts so visitors fetch the new edition).
Usage: python scripts/build-companion-atlas-npcs.py [--fetch]
"""
import collections, itertools, json, math, os, re, statistics, sys, time, urllib.request
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out = os.path.join(root, 'desktop', 'FullAIon2Companion', 'atlas-npcs')
os.makedirs(out, exist_ok=True)
race = {'Cogni': 'C', 'Fera': 'F', 'Natura': 'N', 'Varian': 'V'}
app_dir = os.path.join(root, 'desktop', 'FullAIon2Companion')
pet_of = json.load(open(os.path.join(root, 'desktop', 'vendor', 'Aion-DPS-Meter-084e7e94c5c5c4d280c4547ce9c39168e86d6437', 'Client', 'assets', 'aion2', 'pets', 'pets.json'), encoding='utf-8'))['monsters']
pet_icon = json.load(open(os.path.join(app_dir, 'pet-icons.json'), encoding='utf-8'))
siblings = json.load(open(os.path.join(app_dir, 'npc-siblings.json'), encoding='utf-8'))
fetch = '--fetch' in sys.argv
thgl_list = os.path.join(root, 'research', 'thgl', 'bestiary-es.json')
thgl_dir = os.path.join(app_dir, 'thgl-icons')
# aion2.th.gl's dot and two skulls stand for "no portrait" (641, 83 and 14 of its creatures): not portraits.
thgl_generic = {'5ee309ad.webp', '454ff65d.webp', '1acc87a1.webp'}


def get(url, path):
    request = urllib.request.Request(url, headers={'User-Agent': 'DaevaCompanion/1.0 (+https://governanceai.me)'})
    with urllib.request.urlopen(request, timeout=30) as answer:
        data = answer.read()
    with open(path, 'wb') as f:
        f.write(data)
    time.sleep(0.3)


if fetch:
    os.makedirs(os.path.dirname(thgl_list), exist_ok=True)
    get('https://aion2.th.gl/api/db/sidebar?section=bestiary&locale=es', thgl_list)
thgl_id, thgl_name = {}, {}
if os.path.exists(thgl_list):
    for group in json.load(open(thgl_list, encoding='utf-8'))['groups']:
        for item in group['items']:
            if item['id'].startswith('m_') and item['id'][2:].isdigit() and (item.get('icon') or {}).get('url'):
                thgl_id[int(item['id'][2:])] = item['icon']['url']
                thgl_name.setdefault(re.sub(r'\s*\(Lv \d+\)$', '', item['name']).strip().lower(), set()).add(item['icon']['url'])


def record(n):
    path = os.path.join(root, 'public', 'data', 'game', 'npc', f'{n}.json')
    return json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {}


def portrait(n, marker):
    # The creature record's icon, else the website marker's own (npcIcon), when the app ships it. The website's
    # /assets/catalog portraits are the pet icons the app ships in assets/pets (same file names). UT_Marker_* are the
    # game's generic map markers (a skull, a speech bubble), which the client gives some 4,000 creatures with no portrait
    # (Araña de ojos rojos, 2000923): not portraits.
    for icon in [record(n).get('icon') or '', marker.get('npcIcon') or '']:
        name = os.path.basename(icon)
        if icon.startswith('/assets/game/') and not name.startswith('UT_Marker') and os.path.exists(os.path.join(root, 'public', 'assets', 'game', name)):
            return 'game/' + name
        if icon.startswith('/assets/catalog/') and os.path.exists(os.path.join(app_dir, 'pet-icons', name)):
            return 'pets/' + name
    pet = pet_icon.get(str(pet_of.get(str(n), '')), '')
    if pet.startswith('/assets/pets/') and os.path.exists(os.path.join(app_dir, 'pet-icons', os.path.basename(pet))):
        return pet[len('/assets/'):]
    # Last, aion2.th.gl's family portrait: by creature id, its sibling's, or a name it gives a single portrait (Durgal de la
    # meseta, Tuka, Frugon...). The rest (lava flowers, totems, towers) keep their race crest.
    names = thgl_name.get((record(n).get('name') or '').strip().lower()) or set()
    url = thgl_id.get(n) or thgl_id.get(siblings.get(str(n))) or (next(iter(names)) if len(names) == 1 else None)
    file = os.path.basename(url or '')
    if not file or file in thgl_generic:
        return None
    if fetch and not os.path.exists(os.path.join(thgl_dir, file)):
        os.makedirs(thgl_dir, exist_ok=True)
        get(url, os.path.join(thgl_dir, file))
    return 'thgl/' + file if os.path.exists(os.path.join(thgl_dir, file)) else None


SLUGS = {'1': 'verteron', '2': 'altgard', '3': 'reshanta', '5': 'ishalgen', '6': 'poeta', '7': 'reshanta-mid', '8': 'morheim', '9': 'eltnen', '10': 'reshanta-top'}
RACE_CATEGORY = {'C': 'Cognis', 'F': 'Feras', 'N': 'Naturas', 'V': 'Varians'}
catalog = None


def monsters_of(slug):
    """Catalog monsters with spawn points on this map."""
    global catalog
    if catalog is None:
        catalog = []
        folder = os.path.join(root, 'public', 'data', 'game', 'npc')
        for name in os.listdir(folder):
            if name.endswith('.json'):
                try:
                    d = json.load(open(os.path.join(folder, name), encoding='utf-8'))
                except ValueError:
                    continue
                if d.get('npcType') == 'Monster':
                    catalog.append(d)
    return [d for d in catalog if ((d.get('spawns') or {}).get(slug) or {}).get('points')]


def theil_sen(pairs):
    slopes = sorted((v2 - v1) / (u2 - u1) for (u1, v1), (u2, v2) in itertools.combinations(pairs, 2) if abs(u2 - u1) > 1e-6)
    a = slopes[len(slopes) // 2]
    return a, statistics.median(v - a * u for u, v in pairs)


def least_squares(pairs):
    n = len(pairs); su = sum(u for u, _ in pairs); sv = sum(v for _, v in pairs)
    suu = sum(u * u for u, _ in pairs); suv = sum(u * v for u, v in pairs)
    a = (n * suv - su * sv) / (n * suu - su * su)
    return a, (sv - a * su) / n


def fit(tagged, records, slug):
    """Catalog units -> atlas pixels for this map: (ax, bx, ay, by, median error px, markers paired), or None."""
    seeds = []
    for d in records:
        pts, mks = d['spawns'][slug]['points'], tagged.get(d['id'], [])
        if len(pts) == len(mks) >= 3:
            seeds.append(((sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)),
                          (sum(m[0] for m in mks) / len(mks), sum(m[1] for m in mks) / len(mks))))
    if len(seeds) < 3:
        return None
    ax, bx = theil_sen([(c[0], m[0]) for c, m in seeds]); ay, by = theil_sen([(c[1], m[1]) for c, m in seeds])
    errors = []
    for _ in range(4):
        px, py, errors = [], [], []
        for d in records:
            pts = [(ax * x + bx, ay * y + by, x, y) for x, y in d['spawns'][slug]['points']]
            for mx, my in tagged.get(d['id'], []):
                p = min(pts, key=lambda p: (p[0] - mx) ** 2 + (p[1] - my) ** 2)
                if math.hypot(p[0] - mx, p[1] - my) < 6:
                    px.append((p[2], mx)); py.append((p[3], my)); errors.append(math.hypot(p[0] - mx, p[1] - my))
        if len(px) < 20:
            return None
        ax, bx = least_squares(px); ay, by = least_squares(py)
    return ax, bx, ay, by, statistics.median(errors), len(errors)


def name_key(name):
    return re.sub(r'\s+', ' ', (name or '').strip().lower())


class Near:
    """Points (x, y, value) found by distance, on a 32 px grid."""
    def __init__(self, items):
        self.cells = {}
        for x, y, v in items:
            self.cells.setdefault((int(x // 32), int(y // 32)), []).append((x, y, v))

    def within(self, x, y, r):
        cx, cy, reach = int(x // 32), int(y // 32), int(r // 32) + 1
        return [(a, b, v) for i in range(cx - reach, cx + reach + 1) for j in range(cy - reach, cy + reach + 1)
                for a, b, v in self.cells.get((i, j), ()) if math.hypot(a - x, b - y) <= r]


# ATLAS_LOG=<file> writes every decision as a JSON line (map, kind, monster, id, x, y, gap) for audits.
decisions = open(os.environ['ATLAS_LOG'], 'w', encoding='utf-8') if os.environ.get('ATLAS_LOG') else None


def note(map_id, kind, name, npc, x, y, gap=None):
    if decisions:
        decisions.write(json.dumps({'map': map_id, 'kind': kind, 'name': name, 'npc': npc, 'x': x, 'y': y, 'gap': gap and round(gap, 1)}, ensure_ascii=False) + '\n')


world_fit = {m['id']: m for m in json.load(open(os.path.join(app_dir, 'minimap.json'), encoding='utf-8'))['maps']}
observed_path = os.path.join(root, 'research', 'atlas', 'observed-spawns.tsv')
observed = []
if os.path.exists(observed_path):
    for line in open(observed_path, encoding='utf-8'):
        f = line.rstrip('\n').split('\t')
        if len(f) >= 4 and f[0] in world_fit and f[1].isdigit():
            observed.append((f[0], int(f[1]), float(f[2]), float(f[3])))


def correct_creatures(map_id, app, result):
    """The creature layers with each monster's catalog spawn points, and the places the game showed us (see the top of this
    file). Changes app and result."""
    slug = SLUGS[map_id]
    records = monsters_of(slug)
    creature_cats = [c for c in app['categories'] if c['filter'] == 'criaturas']
    tagged = {}
    for c in creature_cats:
        for m, n in zip(c['markers'], (result.get(c['id']) or {}).get('npcs', [])):
            if n:
                tagged.setdefault(n, []).append((m[0], m[1]))
    f = fit(tagged, records, slug)
    if not f or f[4] > 3:
        print(f'  map {map_id}: catalog fit not good enough ({f and round(f[4], 2)} px), creatures kept as the atlas has them')
        return {}
    for c in creature_cats:
        result.setdefault(c['id'], {'npcs': [0] * len(c['markers']), 'races': '-' * len(c['markers'])})
    ax, bx, ay, by, median, paired = f
    px = lambda x, y: (round(ax * x + bx, 1), round(ay * y + by, 1))
    owners = Near([(*px(x, y), d['id']) for d in records for x, y in d['spawns'][slug]['points']])
    groups = {}
    for d in records:
        groups.setdefault(name_key(d.get('name')), []).append(d)
    group_of = {d['id']: k for k, ds in groups.items() for d in ds}
    ids_of = {k: {d['id'] for d in ds} for k, ds in groups.items()}
    # A website marker is "owned" when it stands on a catalog point of its own monster (any variant of its name).
    owned = lambda x, y, n: bool(n) and n in group_of and any(v in ids_of[group_of[n]] for _, _, v in owners.within(x, y, 3))
    old, every = {}, []  # group -> [(category id, index, x, y, name, npc, race)]; every creature marker as (x, y, (npc, owned))
    for c in creature_cats:
        r = result[c['id']]
        for i, (m, n, rc) in enumerate(zip(c['markers'], r['npcs'], r['races'])):
            every.append((m[0], m[1], (n, owned(m[0], m[1], n))))
            g = group_of.get(n) if n else (name_key(m[2]) if name_key(m[2]) in groups else None)
            if g:
                old.setdefault(g, []).append((c['id'], i, m[0], m[1], m[2], n, rc))
    every = Near(every)
    in_groups = {(o[0], o[1]) for ms in old.values() for o in ms}
    untagged = Near([(m[0], m[1], (c['id'], i, m[0], m[1], m[2], 0, rc)) for c in creature_cats
                     for i, (m, n, rc) in enumerate(zip(c['markers'], result[c['id']]['npcs'], result[c['id']]['races']))
                     if not n and (c['id'], i) not in in_groups])
    claimed = set()
    known = {d['id']: d for d in catalog}
    by_name = {c['name']: c['id'] for c in creature_cats}
    elsewhere = {n for c in app['categories'] if c['filter'] != 'criaturas' for n in (result.get(c['id']) or {}).get('npcs', []) if n}
    drop, retag, add = set(), {}, {}
    stats = collections.Counter()
    far, no_layer = [], set()

    def layer(d, mine):
        # Where this monster's markers were, else its name's, else its race's layer. Ties: the first seen (stable).
        own = [o[0] for o in mine if o[5] == d['id']] or [o[0] for o in mine]
        if own:
            return collections.Counter(own).most_common(1)[0][0]
        return by_name.get(RACE_CATEGORY.get(race.get(d.get('creatureName'), '-'), ''))

    for g, ds in sorted(groups.items()):
        ids = ids_of[g]
        points = []
        for d in sorted(ds, key=lambda d: d['id']):
            for x, y in d['spawns'][slug]['points']:
                p = px(x, y)
                # A pack (3-4 of a monster within a step) is one place, as the website draws it.
                if 0 <= p[0] <= 4096 and 0 <= p[1] <= 4096 and not any(math.hypot(p[0] - q[0], p[1] - q[1]) <= 1.5 for q, _ in points):
                    points.append((p, d))
        if not points:
            continue
        mine = old.get(g, [])
        if not mine and ids & elsewhere:
            continue  # only on another layer (a boss of «Jefes con nombre»)
        if not mine:
            # Untagged website markers standing on its points are its own (13 «Abyss Old Rotan» on Rotan oxidado del Abismo's).
            mine = list(dict.fromkeys(o for p, _ in points for _, _, o in untagged.within(p[0], p[1], 2) if (o[0], o[1]) not in claimed))
            claimed.update((o[0], o[1]) for o in mine)
        if mine:
            nearest = min(math.hypot(o[2] - p[0], o[3] - p[1]) for o in mine for p, _ in points)
            if nearest > 300:
                far.append((ds[0]['name'], round(nearest)))
                continue
        used, adds, shared = {}, [], []
        for p, d in points:
            near = [o for o in mine if math.hypot(o[2] - p[0], o[3] - p[1]) <= 3]
            free = [o for o in near if (o[0], o[1]) not in used]
            if free:
                o = min(free, key=lambda o: math.hypot(o[2] - p[0], o[3] - p[1]))
                used[(o[0], o[1])] = d
                continue
            if near:
                stats['pack'] += 1
                continue  # an old marker of its own already stands on this spot
            # A spawn group's members share their spots in the catalog (the very same point, 0.3 px at most); where the
            # website gave such a spot to another member, standing on that member's own point, it stays that member's.
            if any(n not in ids and is_own and any(v == n for _, _, v in owners.within(p[0], p[1], 0.3))
                   for _, _, (n, is_own) in every.within(p[0], p[1], 2)):
                shared.append((p, d))
                continue
            adds.append((p, d))
        # ...unless that would leave the monster nowhere (the cubs of «Ratón salvaje quemado» share all 15 of its spots).
        if not used and not adds and shared:
            adds, shared = shared, []
            stats['shared-kept'] += len(adds)
        for p, d in shared:
            stats['shared'] += 1
            note(map_id, 'shared', d['name'], d['id'], p[0], p[1])
        for p, d in adds:
            cat = layer(d, mine)
            if cat is None:
                no_layer.add(d['name'])
                continue
            rc = race.get(d.get('creatureName')) or next((o[6] for o in mine if o[0] == cat and o[6] != '-'), '-')
            add.setdefault(cat, []).append(([p[0], p[1], d['name']], d['id'], rc))
            stats['added'] += 1
            note(map_id, 'added', d['name'], d['id'], p[0], p[1])
        # A kept marker stands for the catalog point it sits on: that monster's id, race and name (the website tags all
        # variants with the first one's: Araña de rama seca Nv. 11 on the Nv. 13, 33 and 34 spots).
        for (cid, i), d in used.items():
            o = next(o for o in mine if (o[0], o[1]) == (cid, i))
            retag[(cid, i)] = (d['id'], race.get(d.get('creatureName')) or o[6], d['name'])
            stats['kept'] += 1
        rest = [o for o in mine if (o[0], o[1]) not in used]
        gap, vec = {}, {}
        for o in rest:
            p = min((p for p, _ in points), key=lambda p: math.hypot(o[2] - p[0], o[3] - p[1]))
            gap[(o[0], o[1])] = math.hypot(o[2] - p[0], o[3] - p[1])
            vec[(o[0], o[1])] = (p[0] - o[2], p[1] - o[3])
        # The website drew some camps whole but moved (Brigada de Lagta: all 76 markers 68 px north of the 76 points): one
        # shift that takes most of the monster's loose markers onto its points - the points no kept marker stands on, which
        # a moved camp leaves empty (against all of them, a dense camp votes some shift by chance: the Legion's compound).
        # Votes: every vector from a loose marker to one of those points within 150 px, in 4 px bins; the bin most markers
        # share, if 3 or more and 40 % of them, is the shift (the nearest point is often another one of the camp).
        kept_here = [o for o in mine if (o[0], o[1]) in used]
        empty = [p for p, _ in points if not any(math.hypot(p[0] - o[2], p[1] - o[3]) <= 3 for o in kept_here)]
        loose = [o for o in rest if gap[(o[0], o[1])] <= 150]
        shift = None
        if len(loose) >= 3 and empty:
            votes = {}
            for o in loose:
                for p in empty:
                    v = (p[0] - o[2], p[1] - o[3])
                    if math.hypot(*v) <= 150:
                        votes.setdefault((round(v[0] / 4), round(v[1] / 4)), set()).add((o[0], o[1]))
            fits = lambda b: len(set().union(*(votes.get((b[0] + i, b[1] + j), set()) for i in (-1, 0, 1) for j in (-1, 0, 1))))
            best = max(sorted(votes, key=lambda b: (-len(votes[b]), b))[:60], key=fits)
            if fits(best) >= max(3, 0.4 * len(loose)):
                shift = (best[0] * 4, best[1] * 4)
        # Once voted on the empty points, the shift holds for the whole camp: a moved marker may land on a kept one's point.
        moved = lambda o: shift is not None and any(math.hypot(p[0] - o[2] - shift[0], p[1] - o[3] - shift[1]) <= 8 for p, _ in points)

        def company(o, pool):
            seen, todo = {(o[0], o[1])}, [o]
            while todo:
                a = todo.pop()
                for b in pool:
                    if (b[0], b[1]) not in seen and math.hypot(a[2] - b[2], a[3] - b[3]) <= 80:
                        seen.add((b[0], b[1]))
                        todo.append(b)
            return len(seen)
        bare = [o for o in rest if gap[(o[0], o[1])] <= 150 and not owners.within(o[2], o[3], 20)]
        far_ones = [o for o in rest if gap[(o[0], o[1])] > 150]
        for o in rest:
            k = (o[0], o[1])
            if any(math.hypot(o[2] - u[2], o[3] - u[3]) <= 1.5 for u in kept_here):
                kind = 'copy'  # a second copy of a marker of its own kept on this spot (in another layer, say)
            elif any(n not in ids and is_own for _, _, (n, is_own) in every.within(o[2], o[3], 1.5)):
                kind = 'stacked'  # the website draws it with another monster that stands on its own point: either spawns there
            elif any(v not in ids for _, _, v in owners.within(o[2], o[3], 0.6)):
                kind = 'mislabelled'  # alone on another monster's catalog point: the website swapped names (the spirit stones)
            elif gap[k] <= 150:
                if moved(o):
                    kind = 'displaced'  # part of a camp the website drew moved
                elif o in bare and company(o, bare) >= 3:
                    kind = 'apart'  # 3 or more where the catalog has nothing at all: a camp it lacks (the Legion's compound)
                else:
                    kind = 'displaced'  # the catalog covers this ground: the website's copy, placed off
            elif len(points) >= 8 and gap[k] <= 400 and company(o, far_ones) < 3:
                kind = 'stray'  # a lone label near a camp of the catalog (Klaw infestado, the near side of Reshanta Superior)
            else:
                kind = 'apart'  # a place the catalog lacks
            stats[kind] += 1
            note(map_id, kind, o[4], o[5], o[2], o[3], gap[k])
            if kind not in ('apart', 'stacked'):
                drop.add(k)
        stats['monsters'] += 1
    # The places the game showed us (research/atlas/observed-spawns.tsv, from --spawn-dump of recordings) for monsters the
    # catalog has no points for here: an untagged website marker within 2 px gets the id and name («Balaur Dispatch
    # Researcher» -> Balaur inspector del destacamento); a marker of the name within 15 px tagged with a variant the game
    # never showed here becomes this one (Paphis desgastado Nv. 19, not the website's Nv. 11); else a marker is added.
    w = world_fit[map_id]
    seen_here = {}
    for m, n, x, y in observed:
        if m == map_id:
            p = (round(w['ax'] * x + w['bx'], 1), round(w['ay'] * y + w['by'], 1))
            spots = seen_here.setdefault(n, [])
            if not any(math.hypot(p[0] - a, p[1] - b) <= 1 for a, b in spots):
                spots.append(p)  # every place it was seen: a merged spot let a sighting sit 17 px from any marker
    with_points = {d['id'] for d in records}
    for n, spots in sorted(seen_here.items()):
        d = known.get(n)
        if not d or n in with_points:
            continue
        mine = [(c['id'], i, m[0], m[1], m[2], v, rc) for c in creature_cats
                for i, (m, v, rc) in enumerate(zip(c['markers'], result[c['id']]['npcs'], result[c['id']]['races']))
                if (c['id'], i) not in drop and (v == n or name_key(m[2]) == name_key(d['name']) or retag.get((c['id'], i), (0,))[0] == n)]
        if not mine and n in elsewhere:
            continue  # only on another layer (a boss)
        for p in spots:
            close = [o for o in mine if math.hypot(o[2] - p[0], o[3] - p[1]) <= 15]
            if close:
                if not any(retag.get((o[0], o[1]), (o[5],))[0] == n for o in close):
                    o = min(close, key=lambda o: math.hypot(o[2] - p[0], o[3] - p[1]))
                    v = retag.get((o[0], o[1]), (o[5],))[0]
                    if o[1] >= 0 and v and v != n and v not in with_points and v not in seen_here:
                        retag[(o[0], o[1])] = (n, race.get(d.get('creatureName')) or o[6], d['name'])
                        stats['observed-variant'] += 1
                continue
            hits = [(c['id'], i, m[0], m[1]) for c in creature_cats for i, (m, v) in enumerate(zip(c['markers'], result[c['id']]['npcs']))
                    if not v and (c['id'], i) not in retag and (c['id'], i) not in drop and math.hypot(m[0] - p[0], m[1] - p[1]) <= 2]
            if hits:
                for cid, i, a, b in hits:
                    retag[(cid, i)] = (n, race.get(d.get('creatureName')) or '-', d['name'])
                    mine.append((cid, i, a, b, d['name'], n, '-'))
                    stats['observed-tag'] += 1
                continue
            cat = layer(d, mine)
            if cat is None:
                no_layer.add(d['name'])
                continue
            add.setdefault(cat, []).append(([p[0], p[1], d['name']], n, race.get(d.get('creatureName')) or '-'))
            mine.append((cat, -1, p[0], p[1], d['name'], n, '-'))
            stats['observed-add'] += 1
            note(map_id, 'observed-add', d['name'], n, p[0], p[1])
    # A website label the game named that way names every untagged marker with it (62 more «Balaur Dispatch Researcher»).
    learned = {}
    for c in creature_cats:
        for i, (m, v) in enumerate(zip(c['markers'], result[c['id']]['npcs'])):
            if not v and (c['id'], i) in retag:
                learned.setdefault(m[2], retag[(c['id'], i)])
    for c in creature_cats:
        for i, (m, v) in enumerate(zip(c['markers'], result[c['id']]['npcs'])):
            if not v and (c['id'], i) not in retag and (c['id'], i) not in drop and m[2] in learned:
                retag[(c['id'], i)] = learned[m[2]]
                stats['observed-name'] += 1
    for c in creature_cats:
        r = result[c['id']]
        races = list(r['races'])
        for i in range(len(c['markers'])):
            if (c['id'], i) in retag:
                n, rc, label = retag[(c['id'], i)]
                r['npcs'][i] = n
                races[i] = rc or '-'
                c['markers'][i] = [c['markers'][i][0], c['markers'][i][1], label]
            elif r['npcs'][i] in known and c['markers'][i][2] != known[r['npcs'][i]]['name']:
                # A tagged marker says its monster's catalog name, as the cards do (the website's «Kailin» -> Kailín).
                c['markers'][i] = [c['markers'][i][0], c['markers'][i][1], known[r['npcs'][i]]['name']]
                stats['renamed'] += 1
        keep = [i for i in range(len(c['markers'])) if (c['id'], i) not in drop]
        extra = add.get(c['id'], [])
        c['markers'] = [c['markers'][i] for i in keep] + [m for m, _, _ in extra]
        c['_source'] = keep + [None] * len(extra)  # which website marker each one is (None: added), for the website's file
        r['npcs'] = [r['npcs'][i] for i in keep] + [n for _, n, _ in extra]
        r['races'] = ''.join(races[i] for i in keep) + ''.join(rc for _, _, rc in extra)
        if not any(r['npcs']):
            del result[c['id']]
    print(f"  map {map_id} ({slug}): fit {ax:.5f}x{bx:+.1f} {ay:.5f}y{by:+.1f}, median {median:.2f} px over {paired} markers; "
          + ', '.join(f'{k} {v}' for k, v in sorted(stats.items()))
          + (f"; far, kept as they were: {far}" if far else '') + (f"; no layer: {sorted(no_layer)}" if no_layer else ''))
    return stats


def web_markers(map_id, w, c, r):
    """The website's markers of a corrected creature layer (c, with c['_source']): each kept one as the website had it, with
    the id, name and race of the monster it turned out to be; each added one new, its look taken from a website marker of
    the same monster (else of the layer). Ids stay (a viewer's «visitado» marks are kept by id); new ones are stable."""
    by_npc = {}
    for m in w['markers']:
        n = m.get('npcId') or (m.get('npcVariants') or [{}])[0].get('id')
        if n:
            by_npc.setdefault(n, m)
    common = collections.Counter(m.get('icon') for m in w['markers']).most_common(1)
    out, ids = [], set()
    for (x, y, label), n, src in zip(c['markers'], r['npcs'] if r else [0] * len(c['markers']), c['_source']):
        d = known.get(n) if n else None
        if src is not None:
            m = dict(w['markers'][src])
            was = m.get('npcId') or (m.get('npcVariants') or [{}])[0].get('id') or 0
            if n and n != was:
                m['npcId'] = n
                m.pop('npcVariants', None)
                if d:
                    m['originalName'] = d.get('originalName') or m.get('originalName')
                    if d.get('creatureName'):
                        m['creatureName'] = d['creatureName']
            if label != m['name']:
                if m.get('description') == m['name']:
                    m['description'] = label
                m['name'] = label
        else:
            look = by_npc.get(n) or {}
            m = {'id': f'c{n}-{round(x)}-{round(y)}', 'lat': y, 'lng': x, 'name': label, 'description': label,
                 'icon': look.get('icon') or (common[0][0] if common else w.get('icon')), 'npcId': n}
            if d and d.get('creatureName'):
                m['creatureName'] = d['creatureName']
            for k in ('raceIcon', 'npcIcon'):
                if look.get(k):
                    m[k] = look[k]
            if 'npcIcon' not in m and d and str(d.get('icon') or '').startswith('/assets/game/') and 'UT_Marker' not in d['icon']:
                m['npcIcon'] = d['icon']
            m['originalName'] = (d or {}).get('originalName') or label
        while m['id'] in ids:
            m['id'] += 'b'
        ids.add(m['id'])
        out.append(m)
    return out


source_dir = os.path.join(root, 'research', 'atlas', 'markers-source')
web_source_dir = os.path.join(root, 'research', 'atlas', 'web-source')
known = {}
used = set()
for map_id in ['1', '2', '3', '5', '6', '7', '8', '9', '10']:
    web = json.load(open(os.path.join(web_source_dir, map_id + '.json'), encoding='utf-8'))
    app = json.load(open(os.path.join(source_dir, map_id + '.json'), encoding='utf-8'))
    by_id = {c['id']: c for c in web['categories']}
    result, icons = {}, {}
    aligned = set()
    for c in app['categories']:
        w = by_id.get(c['id'])
        if not w or len(w['markers']) != len(c['markers']):
            continue
        for m, a in zip(w['markers'], c['markers']):
            if abs(m['lng'] - a[0]) >= 1 or abs(m['lat'] - a[1]) >= 1:
                raise SystemExit(f'map {map_id} category {c["id"]}: markers out of order')
        aligned.add(c['id'])
        npcs = [m.get('npcId') or (m.get('npcVariants') or [{}])[0].get('id') or 0 for m in w['markers']]
        if not any(npcs):
            continue
        # A marker without its creature's race takes its category's, as the website's Atlas does (Cognis -> Cogni crest).
        result[c['id']] = {'npcs': npcs, 'races': ''.join(race.get(m.get('creatureName')) or race.get(w.get('originalName'), '-') for m in w['markers'])}
        for n, m in zip(npcs, w['markers']):
            if n and str(n) not in icons and (icon := portrait(n, m)):
                icons[str(n)] = icon
    correct_creatures(map_id, app, result)
    known = {d['id']: d for d in catalog or []}
    for v in result.values():
        for n in v['npcs']:
            if n and str(n) not in icons and (icon := portrait(n, {})):
                icons[str(n)] = icon
    # The website's Atlas gets the same creature layers (public/data/atlas/<map>.json, from research/atlas/web-source).
    for c in app['categories']:
        if '_source' in c:
            if c['id'] in aligned:
                by_id[c['id']]['markers'] = web_markers(map_id, by_id[c['id']], c, result.get(c['id']))
            del c['_source']
    with open(os.path.join(root, 'public', 'data', 'atlas', map_id + '.json'), 'w', encoding='utf-8') as f:
        json.dump(web, f, separators=(',', ':'), ensure_ascii=False)
    with open(os.path.join(app_dir, 'minimap-markers', map_id + '.json'), 'w', encoding='utf-8') as f:
        json.dump(app, f, separators=(',', ':'), ensure_ascii=False)
    used |= {i for i in icons.values() if i.startswith('thgl/')}
    with open(os.path.join(out, map_id + '.json'), 'w', encoding='utf-8') as f:
        json.dump({'categories': result, 'icons': icons}, f, separators=(',', ':'), ensure_ascii=False)
    print(map_id, len(result), 'categories', sum(len(v['npcs']) for v in result.values()), 'markers', len(icons), 'portraits', os.path.getsize(os.path.join(out, map_id + '.json')), 'bytes')
unused = sorted(set(os.listdir(thgl_dir)) - {os.path.basename(i) for i in used}) if os.path.isdir(thgl_dir) else []
print(len(used), 'aion2.th.gl portraits used', '; not used any more:', unused or 'none')
