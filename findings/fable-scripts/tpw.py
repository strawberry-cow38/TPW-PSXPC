import struct, rabbitizer
data=open('/home/ec2-user/tpw/ext/TPW.BIN','rb').read()
BASE=0x80010000
END=BASE+len(data)
def w(addr): return struct.unpack_from('<I',data,addr-BASE)[0]
def dis(addr):
    return rabbitizer.Instruction(w(addr),vram=addr).disassemble()
def ins(addr):
    return rabbitizer.Instruction(w(addr),vram=addr)
def strat(addr):
    e=data.index(b'\0',addr-BASE); return data[addr-BASE:e].decode('latin1')
def dump(a,b):
    for x in range(a,b,4): print(hex(x), '%08x'%w(x), dis(x))
