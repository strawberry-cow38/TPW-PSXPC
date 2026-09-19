import re, sys
p = 'pcsx_rearmed/plugins/gpulib/gpu.c'
s = open(p).read()

fn = r'''
/* TPW: VRAM->VRAM copies (GP0 0x80). A texture page can be RESIDENT in VRAM and
 * never sampled -- entry 0x10C lands at (768..895, 0..511) and 3000 frames of a
 * park drew from it zero times. If that region is a staging area feeding the
 * pages that ARE drawn from, the moves must show up here. Records each distinct
 * (src, dst, size) once; env var TPW_COPYLOG. */
static unsigned int tpw_cseen[4096];
static int tpw_ncseen = 0;
static FILE *tpw_cfp = NULL;
static unsigned long tpw_ncopy = 0;

static void tpw_note_copy(unsigned int sx, unsigned int sy,
                          unsigned int dx, unsigned int dy,
                          unsigned int w, unsigned int h)
{
  unsigned int key = (sx << 22) ^ (sy << 13) ^ (dx << 4) ^ (dy >> 5)
                   ^ (w << 17) ^ (h << 9) ^ (dy << 27);
  int i;
  tpw_ncopy++;
  for (i = 0; i < tpw_ncseen; i++)
    if (tpw_cseen[i] == key) return;
  if (tpw_ncseen >= 4096) return;
  tpw_cseen[tpw_ncseen++] = key;
  if (!tpw_cfp) {
    const char *p = getenv("TPW_COPYLOG");
    if (!p) return;
    tpw_cfp = fopen(p, "w");
    if (!tpw_cfp) return;
    fprintf(tpw_cfp, "# nth sx sy dx dy w h\n");
  }
  fprintf(tpw_cfp, "%lu %u %u %u %u %u %u\n", tpw_ncopy, sx, sy, dx, dy, w, h);
  fflush(tpw_cfp);
}

static void tpw_scan_list'''

anchor = '\nstatic void tpw_scan_list'
assert s.count(anchor) == 1, 'scan_list anchor count %d' % s.count(anchor)
s = s.replace(anchor, fn, 1)

call_anchor = '''    case 0x80:
      if (unlikely((pos+3) >= count)) {
        cmd = -1; // incomplete cmd, can't consume yet
        break;
      }
'''
assert s.count(call_anchor) == 1, 'case 0x80 anchor count %d' % s.count(call_anchor)
s = s.replace(call_anchor, call_anchor + '''      tpw_note_copy(LE32TOH(data[pos + 1]) & 0x3ff,
                    (LE32TOH(data[pos + 1]) >> 16) & 0x1ff,
                    LE32TOH(data[pos + 2]) & 0x3ff,
                    (LE32TOH(data[pos + 2]) >> 16) & 0x1ff,
                    ((LE32TOH(data[pos + 3]) - 1) & 0x3ff) + 1,
                    (((LE32TOH(data[pos + 3]) >> 16) - 1) & 0x1ff) + 1);
''', 1)

open(p, 'w').write(s)
print('patched ok')
