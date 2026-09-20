#!/usr/bin/env python3
"""Disassemble MIPS R3000 out of the game's own image.

    python3 tools/mdis.py IMAGE ADDR N

IMAGE is TPW.BIN's body (the bytes that load at 0x80010000), ADDR a virtual
address, N how many instructions.  Only the subset PSX code actually uses is
decoded; anything else prints as .word, which is honest rather than wrong.
"""
import struct
import sys

BASE = 0x80010000
R = ['zero', 'at', 'v0', 'v1', 'a0', 'a1', 'a2', 'a3', 't0', 't1', 't2', 't3', 't4', 't5', 't6', 't7',
     's0', 's1', 's2', 's3', 's4', 's5', 's6', 's7', 't8', 't9', 'k0', 'k1', 'gp', 'sp', 'fp', 'ra']
IMM = {0x08: 'addi', 0x09: 'addiu', 0x0A: 'slti', 0x0B: 'sltiu', 0x0C: 'andi', 0x0D: 'ori', 0x0E: 'xori'}
MEM = {0x20: 'lb', 0x21: 'lh', 0x23: 'lw', 0x24: 'lbu', 0x25: 'lhu', 0x28: 'sb', 0x29: 'sh', 0x2B: 'sw'}
SPECIAL = {0x00: 'sll', 0x02: 'srl', 0x03: 'sra', 0x04: 'sllv', 0x06: 'srlv', 0x07: 'srav',
           0x08: 'jr', 0x09: 'jalr', 0x10: 'mfhi', 0x12: 'mflo', 0x18: 'mult', 0x19: 'multu',
           0x1A: 'div', 0x1B: 'divu', 0x20: 'add', 0x21: 'addu', 0x22: 'sub', 0x23: 'subu',
           0x24: 'and', 0x25: 'or', 0x26: 'xor', 0x27: 'nor', 0x2A: 'slt', 0x2B: 'sltu'}


def sign(v):
    return v - 0x10000 if v & 0x8000 else v


def one(pc, w):
    op, rs, rt, rd, sh, fn = w >> 26, (w >> 21) & 31, (w >> 16) & 31, (w >> 11) & 31, (w >> 6) & 31, w & 63
    imm, target = w & 0xFFFF, (pc & 0xF0000000) | ((w & 0x3FFFFFF) << 2)
    br = pc + 4 + (sign(imm) << 2)
    if w == 0:
        return 'nop'
    if op == 0:
        m = SPECIAL.get(fn)
        if m is None:
            return '.word 0x%08X' % w
        if m in ('sll', 'srl', 'sra'):
            return '%-7s %s, %s, %d' % (m, R[rd], R[rt], sh)
        if m == 'jr':
            return 'jr      %s' % R[rs]
        if m == 'jalr':
            return 'jalr    %s, %s' % (R[rd], R[rs])
        if m in ('mfhi', 'mflo'):
            return '%-7s %s' % (m, R[rd])
        if m in ('mult', 'multu', 'div', 'divu'):
            return '%-7s %s, %s' % (m, R[rs], R[rt])
        return '%-7s %s, %s, %s' % (m, R[rd], R[rs], R[rt])
    if op == 1:
        m = {0: 'bltz', 1: 'bgez', 16: 'bltzal', 17: 'bgezal'}.get(rt, 'b?%d' % rt)
        return '%-7s %s, 0x%08X' % (m, R[rs], br)
    if op == 2:
        return 'j       0x%08X' % target
    if op == 3:
        return 'jal     0x%08X' % target
    if op in (4, 5):
        return '%-7s %s, %s, 0x%08X' % ('beq' if op == 4 else 'bne', R[rs], R[rt], br)
    if op in (6, 7):
        return '%-7s %s, 0x%08X' % ('blez' if op == 6 else 'bgtz', R[rs], br)
    if op in IMM:
        return '%-7s %s, %s, %d' % (IMM[op], R[rt], R[rs], sign(imm) if op < 0x0C else imm)
    if op == 0x0F:
        return 'lui     %s, 0x%04X' % (R[rt], imm)
    if op in MEM:
        return '%-7s %s, %d(%s)' % (MEM[op], R[rt], sign(imm), R[rs])
    return '.word 0x%08X' % w


def main():
    image, addr, count = sys.argv[1], int(sys.argv[2], 0), int(sys.argv[3], 0)
    data = open(image, 'rb').read()
    off = addr - BASE
    for i in range(count):
        pc = addr + i * 4
        w = struct.unpack_from('<I', data, off + i * 4)[0]
        print('%08X  %08X  %s' % (pc, w, one(pc, w)))


if __name__ == '__main__':
    main()
