p='/home/ec2-user/tpw/pcsx_rearmed/plugins/gpulib/gpu.c'
s=open(p).read()
old='''unsigned long tpw_wind[3] = {0,0,0};   /* [0]=CW(neg) [1]=CCW(pos) [2]=degenerate */

static void tpw_note_winding(int x0,int y0,int x1,int y1,int x2,int y2)
{
  long a = (long)(x1-x0)*(y2-y0) - (long)(x2-x0)*(y1-y0);
  if (a > 0) tpw_wind[1]++; else if (a < 0) tpw_wind[0]++; else tpw_wind[2]++;
}'''
assert s.count(old)==1
s=s.replace(old,'''unsigned long tpw_wind[3] = {0,0,0};   /* [0]=neg [1]=pos [2]=degenerate */
/* per texture-page-x, so UI pages can be separated from park geometry */
unsigned long tpw_wind_pg[16][3];

static void tpw_note_winding(int px,int x0,int y0,int x1,int y1,int x2,int y2)
{
  long a = (long)(x1-x0)*(y2-y0) - (long)(x2-x0)*(y1-y0);
  int k = (a > 0) ? 1 : (a < 0) ? 0 : 2;
  int c = (px >> 6) & 15;
  tpw_wind[k]++;
  tpw_wind_pg[c][k]++;
}''',1)

old2='''            tpw_note_winding(ax,ay,bx,by,cx,cy);'''
assert s.count(old2)==1
s=s.replace(old2,'''            tpw_note_winding((tp & 0xf) * 64, ax,ay,bx,by,cx,cy);''',1)

old3='''  fprintf(stderr, "[tpw] textured-poly winding: negative=%lu positive=%lu degenerate=%lu\\n",
          tpw_wind[0], tpw_wind[1], tpw_wind[2]);'''
assert s.count(old3)==1
s=s.replace(old3, old3 + '''
  {
    int c;
    for (c = 0; c < 16; c++)
      if (tpw_wind_pg[c][0] | tpw_wind_pg[c][1] | tpw_wind_pg[c][2])
        fprintf(stderr, "[tpw]   page x=%-4d neg=%-8lu pos=%-8lu deg=%lu\\n",
                c*64, tpw_wind_pg[c][0], tpw_wind_pg[c][1], tpw_wind_pg[c][2]);
  }''',1)
open(p,'w').write(s); print('patched')
