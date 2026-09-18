import struct,sys
R=['zero','at','v0','v1','a0','a1','a2','a3','t0','t1','t2','t3','t4','t5','t6','t7',
   's0','s1','s2','s3','s4','s5','s6','s7','t8','t9','k0','k1','gp','sp','fp','ra']
def dis(w,pc):
    op=w>>26; rs=(w>>21)&31; rt=(w>>16)&31; rd=(w>>11)&31; sa=(w>>6)&31; fn=w&63
    imm=w&0xffff; simm=imm-0x10000 if imm&0x8000 else imm
    tgt=(pc&0xF0000000)|((w&0x3ffffff)<<2)
    br=pc+4+simm*4
    if op==0:
        if w==0: return 'nop'
        m={0:'sll',2:'srl',3:'sra'}
        if fn in m: return f'{m[fn]} {R[rd]},{R[rt]},{sa}'
        m={4:'sllv',6:'srlv',7:'srav'}
        if fn in m: return f'{m[fn]} {R[rd]},{R[rt]},{R[rs]}'
        if fn==8: return f'jr {R[rs]}'
        if fn==9: return f'jalr {R[rd]},{R[rs]}'
        if fn==12: return 'syscall'
        if fn==13: return 'break'
        if fn==16: return f'mfhi {R[rd]}'
        if fn==17: return f'mthi {R[rs]}'
        if fn==18: return f'mflo {R[rd]}'
        if fn==19: return f'mtlo {R[rs]}'
        m={24:'mult',25:'multu',26:'div',27:'divu'}
        if fn in m: return f'{m[fn]} {R[rs]},{R[rt]}'
        m={32:'add',33:'addu',34:'sub',35:'subu',36:'and',37:'or',38:'xor',39:'nor',42:'slt',43:'sltu'}
        if fn in m: return f'{m[fn]} {R[rd]},{R[rs]},{R[rt]}'
        return f'.word 0x{w:08x}'
    if op==1:
        m={0:'bltz',1:'bgez',16:'bltzal',17:'bgezal'}
        return f'{m.get(rt,"regimm?")} {R[rs]},0x{br:08x}'
    if op==2: return f'j 0x{tgt:08x}'
    if op==3: return f'jal 0x{tgt:08x}'
    if op==4: return f'beq {R[rs]},{R[rt]},0x{br:08x}'
    if op==5: return f'bne {R[rs]},{R[rt]},0x{br:08x}'
    if op==6: return f'blez {R[rs]},0x{br:08x}'
    if op==7: return f'bgtz {R[rs]},0x{br:08x}'
    m={8:'addi',9:'addiu',10:'slti',11:'sltiu'}
    if op in m: return f'{m[op]} {R[rt]},{R[rs]},{simm:#x}' if simm>=0 else f'{m[op]} {R[rt]},{R[rs]},-{-simm:#x}'
    m={12:'andi',13:'ori',14:'xori'}
    if op in m: return f'{m[op]} {R[rt]},{R[rs]},0x{imm:x}'
    if op==15: return f'lui {R[rt]},0x{imm:x}'
    if op==16:
        if rs==0: return f'mfc0 {R[rt]},${rd}'
        if rs==4: return f'mtc0 {R[rt]},${rd}'
        if rs==16: return 'rfe' if fn==16 else f'cop0 0x{w&0x1ffffff:x}'
        return f'cop0 0x{w:08x}'
    if op==18:
        if rs==0: return f'mfc2 {R[rt]},$gte{rd}'
        if rs==2: return f'cfc2 {R[rt]},$gtec{rd}'
        if rs==4: return f'mtc2 {R[rt]},$gte{rd}'
        if rs==6: return f'ctc2 {R[rt]},$gtec{rd}'
        return f'cop2 0x{w&0x1ffffff:x}'
    m={32:'lb',33:'lh',34:'lwl',35:'lw',36:'lbu',37:'lhu',38:'lwr',40:'sb',41:'sh',42:'swl',43:'sw',46:'swr',50:'lwc2',58:'swc2'}
    if op in m: return f'{m[op]} {R[rt]},{simm}({R[rs]})'
    return f'.word 0x{w:08x}'

def dump(path,base,hdr,start=None,end=None,out=sys.stdout):
    d=open(path,'rb').read()
    n=len(d)-hdr
    s=start if start is not None else 0
    e=end if end is not None else n
    for off in range(s,e,4):
        w=struct.unpack_from('<I',d,hdr+off)[0]
        pc=base+off
        print(f'{pc:08x}: {w:08x}  {dis(w,pc)}',file=out)
if __name__=='__main__':
    dump(sys.argv[1],int(sys.argv[2],16),int(sys.argv[3],16))
