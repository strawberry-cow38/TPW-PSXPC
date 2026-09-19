#!/usr/bin/env python3
"""disasm.py <addr> [count] -- disassemble TPW.BIN at a PSX virtual address.

TPW.BIN is raw code with no PS-EXE header, so the load base cannot be read out of the file.
It is 0x80010000, and that is CHECKED rather than trusted: under the right base the track
dispatch table at 0x800DDD78 must read as a run of code pointers, and it does (15/16, the
type handlers 0x8002c9a0 / 0x8002ca30 / ...). A wrong base fails this and the tool refuses
to disassemble rather than printing plausible nonsense at the wrong address.
"""
import struct, sys
from capstone import Cs, CS_ARCH_MIPS, CS_MODE_MIPS32, CS_MODE_LITTLE_ENDIAN

BIN  = '/home/ec2-user/tpw/ext/TPW.BIN'
BASE = 0x80010000
JUMPTAB = 0x800DDD78          # the game's track-size dispatch, fable/folio.md 3.4

def load():
    d = open(BIN, 'rb').read()
    off = JUMPTAB - BASE
    w = struct.unpack_from('<16I', d, off)
    good = sum(1 for x in w if BASE <= x < 0x80100000)
    if good < 12:
        raise SystemExit(f"base {BASE:#x} rejected: jump table reads {good}/16 code pointers")
    return d

def dis(addr, n=60):
    d = load()
    off = addr - BASE
    if not (0 <= off < len(d)):
        raise SystemExit(f"{addr:#x} outside {BASE:#x}..{BASE+len(d):#x}")
    md = Cs(CS_ARCH_MIPS, CS_MODE_MIPS32 | CS_MODE_LITTLE_ENDIAN)
    md.skipdata = True
    return [f"{i.address:08x}: {i.mnemonic:<9s} {i.op_str}"
            for i in md.disasm(d[off:off + n * 4], addr)]

if __name__ == '__main__':
    print('\n'.join(dis(int(sys.argv[1], 16), int(sys.argv[2]) if len(sys.argv) > 2 else 60)))
