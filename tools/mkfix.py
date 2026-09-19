import json, collections, os
CAPS = {
 'park_ride':   'clutlog.txt',
 'park_trail2': '/tmp/hook_clut.txt',
}
draws=[]; per_cap=collections.Counter()
for cap,f in CAPS.items():
    if not os.path.exists(f): continue
    for l in open(f):
        if l.startswith('#'): continue
        p=l.split()
        if len(p)<9: continue
        cx,cy,px,py,bpp,u0,v0,u1,v1=map(int,p[:9])
        draws.append(dict(capture=cap, tpage=[px,py], clut=[cx,cy], bpp=bpp,
                          uv=[u0,v0,u1,v1]))
        per_cap[cap]+=1

def cpage(cx,cy): return [ (cx//64)*64, 0 if cy<256 else 256 ]
pages   = sorted({tuple(d['tpage']) for d in draws})
holders = sorted({tuple(cpage(*d['clut'])) for d in draws})
mapping = collections.defaultdict(set)
rows    = collections.defaultdict(set)
for d in draws:
    mapping['%d,%d'%tuple(d['tpage'])].add('%d,%d'%tuple(cpage(*d['clut'])))
    hp = cpage(*d['clut'])
    rows['%d,%d'%tuple(hp)].add(d['clut'][1]-hp[1])

out = {
 '_what': 'Draw calls observed on real PSX hardware (pcsx_rearmed patched to log every '
          'textured primitive\'s CLUT and texture page as the GPU received it). '
          'These are FIXTURES for verifying a file-side mesh parser: a parser that '
          'reads per-face clut/tpage from FOLIO.GAZ should reproduce these pairings. '
          'No pixels and no palette colours are included.',
 '_how_to_use': 'For each mesh face your parser yields, form (tpage, clut). The set of '
          'distinct (tpage -> clut-page) pairings your parser produces, restricted to '
          'content that appears in these captures, must be a subset of "tpage_to_palette_page" '
          'below. A pairing outside it is a parser bug OR content my camera never saw -- '
          'those two are NOT distinguishable from this file alone, so treat a mismatch as '
          '"investigate", not "fail".',
 '_known_blind_spot': 'Every capture here is build-mode or menu. NO GUEST was ever on screen, '
          'so pages 768 and 832 (the guest sprite banks) appear nowhere below despite being '
          'real. Absence in this file is NOT evidence of absence in the game.',
 'captures': dict(per_cap),
 'texture_pages_observed': ['%d,%d'%p for p in pages],
 'palette_holding_pages':  ['%d,%d'%p for p in holders],
 'tpage_to_palette_page':  {k:sorted(v) for k,v in sorted(mapping.items())},
 'palette_rows_within_holder': {k:sorted(v) for k,v in sorted(rows.items())},
 'draws': draws,
}
json.dump(out, open('hardware_draw_fixtures.json','w'), indent=1)
print('draws:', len(draws), 'from', dict(per_cap))
print('pages observed :', out['texture_pages_observed'])
print('palette holders:', out['palette_holding_pages'])
print('rows in holders:', out['palette_rows_within_holder'])
