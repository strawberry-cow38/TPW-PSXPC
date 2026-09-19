p='/home/ec2-user/tpw/pcsx_rearmed/plugins/gpulib/gpu.c'
s=open(p).read()
anchor='''extern unsigned long tpw_path_used[3];'''
assert s.count(anchor)==1
s=s.replace(anchor, '''/* TPW: winding of textured polygons AS THE GPU RECEIVES THEM. The PSX has no
 * hardware backface cull -- the game decides -- so "what winding does the file
 * use" is only answerable by looking at what actually reaches the GPU. Signed
 * area of the screen-space triangle: >0 one way round, <0 the other. */
unsigned long tpw_wind[3] = {0,0,0};   /* [0]=CW(neg) [1]=CCW(pos) [2]=degenerate */

static void tpw_note_winding(int x0,int y0,int x1,int y1,int x2,int y2)
{
  long a = (long)(x1-x0)*(y2-y0) - (long)(x2-x0)*(y1-y0);
  if (a > 0) tpw_wind[1]++; else if (a < 0) tpw_wind[0]++; else tpw_wind[2]++;
}

extern unsigned long tpw_path_used[3];''',1)

old='''        if (umin <= umax)
          tpw_note_clut((unsigned short)(list[pos + 2] >> 16), tp,
                        umin, vmin, umax, vmax);'''
assert s.count(old)==1
s=s.replace(old, old + '''
        /* vertex words: flat poly 1,3,5(,7); gouraud 1,4,7(,10) */
        if (op < 0x40) {
          int vs = (op & 0x10) ? 3 : 2;
          int i0 = 1, i1 = 1 + vs, i2 = 1 + 2*vs;
          if (pos + i2 < count) {
            int ax = (int)(short)(list[pos+i0] & 0xffff), ay = (int)(short)(list[pos+i0] >> 16);
            int bx = (int)(short)(list[pos+i1] & 0xffff), by = (int)(short)(list[pos+i1] >> 16);
            int cx = (int)(short)(list[pos+i2] & 0xffff), cy = (int)(short)(list[pos+i2] >> 16);
            tpw_note_winding(ax,ay,bx,by,cx,cy);
          }
        }''',1)

old2='''  fprintf(stderr, "[tpw] cmd-list paths carried work: frameskip=%lu async=%lu plain=%lu\\n",
          tpw_path_used[0], tpw_path_used[1], tpw_path_used[2]);'''
assert s.count(old2)==1
s=s.replace(old2, old2 + '''
  fprintf(stderr, "[tpw] textured-poly winding: negative=%lu positive=%lu degenerate=%lu\\n",
          tpw_wind[0], tpw_wind[1], tpw_wind[2]);''',1)
open(p,'w').write(s); print('patched')
