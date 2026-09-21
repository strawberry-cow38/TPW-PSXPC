#!/usr/bin/env python3
"""Decode the already READ OVL11 selection table (objbytes.md §3).

Reuse audit_scenario's extractor; no new disassembly census or overlay decoder.
The JSON fixture carries 19 rows: 8 nodes + 11 links, with raw bytes for checking
the decoded fields. Bytes outside those named fields remain uninterpreted.
"""
import json
import struct
from pathlib import Path
from audit_scenario import overlays, EXT, OVL_BASE, sha, hx

ROOT = Path(__file__).resolve().parents[1]
TABLE = 0x801141F4
STRIDE = 28
ROWS, NODES, LINKS = 19, 8, 11


def main():
    packed = (EXT / 'TPW.OVL').read_bytes()
    expanded = overlays(packed)
    assert len(expanded) == 12
    data, metadata = expanded[11]
    start = TABLE - OVL_BASE
    raw = data[start:start + ROWS * STRIDE]
    assert len(raw) == ROWS * STRIDE
    nodes, links, rows = [], [], []
    for index in range(ROWS):
        row = raw[index * STRIDE:(index + 1) * STRIDE]
        kind = struct.unpack_from('<H', row)[0]
        rows.append(dict(index=index, address=hx(TABLE + index * STRIDE), hex=row.hex()))
        if index < NODES:
            assert kind == 0
            nodes.append(dict(index=index, world=row[6], park=row[7],
                              name_text_id=struct.unpack_from('<H', row, 10)[0]))
        else:
            assert kind == 1
            links.append(dict(row=index, a=row[24], b=row[25], cost=row[26]))
    assert len(nodes) == NODES and len(links) == LINKS
    # Shape/negative controls; exact topology and names are pinned independently by C# tests.
    assert len({(n['world'], n['park']) for n in nodes}) == NODES
    assert all(0 <= l['a'] < NODES and 0 <= l['b'] < NODES for l in links)
    assert not any({l['a'], l['b']} == {0, 7} for l in links)
    result = dict(source='findings/objbytes.md §3', overlay_base=hx(OVL_BASE),
        packed_sha256=sha(packed), overlay=metadata, table_address=hx(TABLE),
        row_stride=STRIDE, row_count=ROWS, node_count=NODES, link_count=LINKS,
        skipped_rows=0, table_sha256=sha(raw), rows=rows, nodes=nodes, links=links,
        limitations=['Decodes existing READ fields; does not re-derive selection control flow',
                     'No console UI/input or save-card measurement'])
    (ROOT / 'findings/selection-audit.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({k: result[k] for k in ('row_count', 'node_count', 'link_count', 'skipped_rows', 'table_sha256')}))


if __name__ == '__main__':
    main()
