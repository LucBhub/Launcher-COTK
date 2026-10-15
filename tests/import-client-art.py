"""Read-only inventory/extraction of the selected PS3 client's image sheet and pack2 assets.
Uses the existing project's pack2/CRC reader. Writes only launcher assets + provenance.
"""
import argparse, importlib.util, json, io, re, hashlib
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('packreader', r'D:\projects\COTK\tools\qa\footwear_shred_overlay.py')
reader = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
ap = argparse.ArgumentParser()
ap.add_argument('--client', default=r'D:\Games\client')
ap.add_argument('--inventory', action='store_true')
args = ap.parse_args()
packs = Path(args.client) / 'Resources' / 'Assets'
index = {}
for path in sorted(packs.glob('*.pack')):
    with path.open('rb') as fh: entries = reader.pack_index(fh, path.stat().st_size)
    for entry in entries: index[entry['name']] = (path, entry)
names = list(index)
matches = sorted(set(n for n in names if re.search(r'rank|tier|bronze|silver|platinum|royalty|diamond', n, re.I)))
if args.inventory:
    print('\n'.join(matches))
    print(f'Indexed {len(index)} assets; {len(matches)} rank candidates')
    for name in names:
        if name == 'MatchRankImageSets.txt':
            path, entry = index[name]
            print(reader.read_member(path, entry).decode('utf-8-sig'))
    print('Image sheets:', [n for n in names if re.search(r'^Image.*\.txt$', n)])
else:
    dest = ROOT / 'assets' / 'ranks'
    dest.mkdir(parents=True, exist_ok=True)
    report = []
    for tier in range(1, 7):
        name = f'icon_tier_{tier:02d}.dds'
        path, entry = index[name]
        data = reader.read_member(path, entry)
        image = Image.open(io.BytesIO(data)).convert('RGBA')
        image.save(dest / f'tier-banner-{tier}.png')
        # Médaillon central découpé proprement en cercle (bord anti-crénelé sur 4x), centre mesuré sur la bannière.
        from PIL import ImageDraw
        cx, cy, radius = image.width / 2, image.height / 2, 45
        crop = (int(cx - 50), 0, int(cx + 50), 100)
        medal = image.crop(crop)
        scale = 4
        mask = Image.new('L', (100 * scale, 100 * scale), 0)
        ImageDraw.Draw(mask).ellipse((50 * scale - radius * scale, 50 * scale - radius * scale, 50 * scale + radius * scale, 50 * scale + radius * scale), fill=255)
        medal.putalpha(mask.resize((100, 100), Image.Resampling.LANCZOS))
        medal.save(dest / f'tier-{tier}.png')
        report.append(dict(tier=tier, resource=name, pack=path.name, offset=entry['offset'], length=entry['length'], crc32=entry['crc32'], sourceSha256=hashlib.sha256(data).hexdigest(), pngSha256=hashlib.sha256((dest/f'tier-{tier}.png').read_bytes()).hexdigest(), size=image.size, crop=crop))
    (dest / 'provenance.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))
