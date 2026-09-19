from PIL import Image
import json, collections

VRAM = open('/tmp/vs_park_ride/vram_000020.bin','rb').read()
W = 1024
def hw(x, y):
    o = (y*W + x)*2
    return VRAM[o] | (VRAM[o+1] << 8)

def clut(cx, cy):
    pal = []
    for i in range(16):
        v = hw(cx + i, cy)
        r = (v & 0x1f) << 3; g = ((v >> 5) & 0x1f) << 3; b = ((v >> 10) & 0x1f) << 3
        pal.append((r, g, b, 0 if i == 0 else 255))
    return pal

def draw(px, py, u0, v0, u1, v1, cx, cy):
    w, h = u1-u0+1, v1-v0+1
    if w < 2 or h < 2 or w > 256 or h > 256: return None
    pal = clut(cx, cy)
    im = Image.new('RGBA', (w, h))
    pix = im.load()
    for yy in range(h):
        for xx in range(w):
            u = u0+xx; v = v0+yy
            word = hw(px + (u >> 2), py + v)
            nib = (word >> ((u & 3) * 4)) & 0xf
            pix[xx, yy] = pal[nib]
    return im

rects = []
for line in open('clutlog.txt'):
    if line.startswith('#'): continue
    f = line.split()
    if len(f) < 9: continue
    cx, cy, px, py, bpp, u0, v0, u1, v1 = map(int, f[:9])
    if bpp != 0: continue
    rects.append((px, py, u0, v0, u1, v1, cx, cy))

print('rects to draw:', len(rects))
ims = []
for r in rects:
    im = draw(*r)
    if im: ims.append((im, r))
print('drawn:', len(ims))

# contact sheet
COLS = 12
cell = 72
rows = (len(ims)+COLS-1)//COLS
sheet = Image.new('RGB', (COLS*cell, rows*cell), (30,30,34))
for i,(im,r) in enumerate(ims):
    im.thumbnail((cell-4, cell-4), Image.NEAREST)
    bg = Image.new('RGB', im.size, (30,30,34)); bg.paste(im, (0,0), im)
    sheet.paste(bg, ((i%COLS)*cell+2, (i//COLS)*cell+2))
sheet = sheet.resize((sheet.width*2, sheet.height*2), Image.NEAREST)
sheet.save('/tmp/parkride_rects.png')
print('sheet', sheet.size)
