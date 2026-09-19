from PIL import Image
exec(open('/tmp/big.py').read().split('rects=[]')[0])
rects=[]
for l in open('clutlog.txt'):
    if l.startswith('#'): continue
    f=l.split()
    if len(f)<9: continue
    cx,cy,px,py,bpp,u0,v0,u1,v1=map(int,f[:9])
    if bpp: continue
    rects.append((px,py,u0,v0,u1,v1,cx,cy))
scored=[]
for r in rects:
    im=draw(*r)
    if im.width<8 or im.height<8: continue
    px_=list(im.convert('RGB').getdata())
    n=len(px_)
    R=sum(p[0] for p in px_)/n; G=sum(p[1] for p in px_)/n; B=sum(p[2] for p in px_)/n
    # ginger fur / pink skin: red dominant, blue low
    if R>90 and R-B>40 and G<R:
        scored.append((R-B, r, im))
scored.sort(key=lambda t:-t[0])
sel=scored[:12]
print('orange/pink-dominant rects:', len(scored))
cell=140
sheet=Image.new('RGB',(6*cell,2*cell),(30,30,34))
for i,(_,r,im) in enumerate(sel):
    im=im.resize((cell-8,cell-8), Image.NEAREST)
    bg=Image.new('RGB',im.size,(30,30,34)); bg.paste(im,(0,0),im)
    sheet.paste(bg,((i%6)*cell+4,(i//6)*cell+4))
sheet.save('/tmp/ride_fur.png'); print(sheet.size)
