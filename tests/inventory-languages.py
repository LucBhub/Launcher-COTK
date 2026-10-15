"""Inventory of French UI strings for localization review (not part of the build)."""
from pathlib import Path
import re, json
root=Path(__file__).resolve().parents[1]
files=list((root/'src'/'UI'/'Views').glob('*.cs'))+[root/'src'/p for p in ['LauncherApp.cs','Core/Errors.cs','UI/MainWindow.cs','UI/Controls/Overlays.cs','UI/Controls/SupportDialogs.cs','UI/Controls/Widgets.cs','Core/Game/PlaySession.cs','Core/Updates/ClientInstaller.cs','Core/ApiClient.cs','Core/Updates/Manifest.cs']]
found={}
for file in files:
    for line in file.read_text(encoding='utf-8-sig').splitlines():
        if line.lstrip().startswith('//') or 'Log.' in line: continue
        for m in re.finditer(r'(?<!\\)"((?:[^"\\]|\\.)*)"',line):
            value=m.group(1)
            if not re.search(r'[A-Za-zÀ-ÿ]{3}',value): continue
            if re.search(r'^(?:api/|E-|[a-zA-Z0-9_]+\.\w+$|#|https?://|M\d)',value): continue
            if re.fullmatch(r'[a-z][A-Za-z0-9_-]*',value): continue
            value=value.replace(r'\n','\n').replace(r'\"','"')
            found.setdefault(value,file.name)
(root/'docs'/'i18n-inventory.json').write_text(json.dumps(found,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'{len(found)} strings inventoried')
