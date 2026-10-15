"""Preserve the original COTK icon artwork; retint chromatic pixels to ember orange.
Generate native 16..256px ICO frames and a preview; never modify the old launcher icon.
"""
import colorsys
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[1]
source=Image.open(r'D:\projects\zemu\launcher\src\cotk.ico').convert('RGBA')
pixels=[]
target=colorsys.rgb_to_hsv(255/255,106/255,43/255)[0]
for r,g,b,a in source.getdata():
    h,s,v=colorsys.rgb_to_hsv(r/255,g/255,b/255)
    if s>0.15 and v>0.12:
        r,g,b=(round(c*255) for c in colorsys.hsv_to_rgb(target,s,v))
    pixels.append((r,g,b,a))
source.putdata(pixels)
source=source.resize((256,256),Image.Resampling.LANCZOS)
source.save(root/'assets'/'cotk.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
source.save(root/'docs'/'icon-orange.png')
print('ICO frames:', sorted(Image.open(root/'assets'/'cotk.ico').ico.sizes()))
