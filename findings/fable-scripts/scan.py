import sys; sys.path.insert(0,'.')
from tpw import *
import rabbitizer, struct
N=len(data)//4
insns=[rabbitizer.Instruction(struct.unpack_from('<I',data,i*4)[0],vram=BASE+i*4) for i in range(N)]
def callers(target):
    out=[]
    for i,ins in enumerate(insns):
        if ins.isValid() and ins.getOpcodeName()=='jal':
            t=ins.getInstrIndexAsVram()
            if t==target: out.append(BASE+i*4)
    return out
def func_start(addr):
    # walk back to find 'addiu $sp,$sp,-X' prologue
    a=addr
    while a>BASE:
        ins=insns[(a-BASE)//4]
        if ins.isValid() and ins.getOpcodeName()=='addiu' and ins.rt==rabbitizer.RegGprO32.sp and ins.rs==rabbitizer.RegGprO32.sp and ins.getProcessedImmediate()<0:
            return a
        a-=4
    return None
def const_of(i, reg, back=12):
    """look back from insn i for a constant load into reg; returns int or None/'?'"""
    for j in range(i-1,max(-1,i-back),-1):
        pj=insns[j]
        if not pj.isValid(): return None
        nm=pj.getOpcodeName()
        if nm in ('addiu','ori') and pj.rt==reg and pj.rs==rabbitizer.RegGprO32.zero:
            return pj.getProcessedImmediate()
        if nm=='addu' and pj.rd==reg and pj.rs==rabbitizer.RegGprO32.zero and pj.rt==rabbitizer.RegGprO32.zero: return 0
        if nm=='lui' and pj.rt==reg: return '?'
        if (pj.modifiesRt() and getattr(pj,'rt',None)==reg) or (pj.modifiesRd() and getattr(pj,'rd',None)==reg): return '?'
        if nm in ('jal','jalr'): 
            if reg in (rabbitizer.RegGprO32.v0,rabbitizer.RegGprO32.v1): return '?'
    return '?'
