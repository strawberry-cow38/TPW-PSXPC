from PIL import Image
VRAM=open('/tmp/vs_park_ride/vram_000020.bin','rb').read(); W=1024
def hw(x,y):
    o=(y*W+x)*2; return VRAM[o]|(VRAM[o+1]<<8)
def pal(cx,cy):
    out=[]
    for i in range(16):
        v=hw(cx+i,cy)
        out.append((((v&0x1f)<<3),(((v>>5)&0x1f)<<3),(((v>>10)&0x1f)<<3),0 if i==0 else 255))
    return out
def draw(px,py,u0,v0,u1,v1,cx,cy):
    w,h=u1-u0+1,v1-v0+1
    p=pal(cx,cy); im=Image.new('RGBA',(w,h)); q=im.load()
    for yy in range(h):
        for xx in range(w):
            u,v=u0+xx,v0+yy
            word=hw(px+(u>>2),py+v); q[xx,yy]=p[(word>>((u&3)*4))&0xf]
    return im
rects=[]
for l in open('clutlog.txt'):
    if l.startswith('#'): continue
    f=l.split()
    if len(f)<9: continue
    cx,cy,px,py,bpp,u0,v0,u1,v1=map(int,f[:9])
    if bpp: continue
    rects.append((px,py,u0,v0,u1,v1,cx,cy))
# pick the biggest ones by area
rects.sort(key=lambda r:-( (r[4]-r[2]+1)*(r[5]-r[3]+1) ))
sel=rects[20:32]
cell=140
sheet=Image.new('RGB',(6*cell,2*cell),(30,30,34))
for i,r in enumerate(sel):
    im=draw(*r); im=im.resize((cell-8,cell-8), Image.NEAREST)
    bg=Image.new('RGB',im.size,(30,30,34)); bg.paste(im,(0,0),im)
    sheet.paste(bg,((i%6)*cell+4,(i//6)*cell+4))
sheet.save('/tmp/ride_big.png'); print(sheet.size)
