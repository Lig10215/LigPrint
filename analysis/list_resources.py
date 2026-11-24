"""List names in the three Qt v2 resource trees registered by the supplied DLabel.exe."""
import struct
import sys
import zlib
from pathlib import Path
import pefile
pe = pefile.PE(sys.argv[1], fast_load=True)
raw = pe.__data__
def offset(va):
    return pe.get_offset_from_rva(va - pe.OPTIONAL_HEADER.ImageBase)
for call in (0xcc771, 0xcc7a1, 0xcc7d1):
    data, names, tree = (offset(struct.unpack_from("<I", raw, call-k)[0]) for k in (16,11,6))
    def walk(index, prefix):
        pos = tree + 22*index
        no, flags = struct.unpack_from(">IH", raw, pos)
        length = struct.unpack_from(">H", raw, names+no)[0]
        name = raw[names+no+6:names+no+6+length*2].decode("utf-16-be") if index else ""
        full = prefix + "/" + name if name else prefix
        if flags & 2:
            count, first = struct.unpack_from(">II", raw, pos+6)
            for child in range(first,first+count):
                walk(child,full)
        else:
            at = data + struct.unpack_from(">I",raw,pos+10)[0]
            print(hex(at), full)
    walk(0,"")

