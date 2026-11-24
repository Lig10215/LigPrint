"""Targeted read-only inspection. Requires pefile and capstone for this analysis only."""
import sys
from pathlib import Path
from hashlib import sha256
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86_const import X86_OP_MEM

root = Path(sys.argv[1])
wrapper = pefile.PE(str(root / "DLibXls.dll"))
library = pefile.PE(str(root / "libxls.dll"))
exports = {s.ordinal: s.name.decode() for s in library.DIRECTORY_ENTRY_EXPORT.symbols if s.name}
imports = {s.address: exports[s.ordinal] for d in wrapper.DIRECTORY_ENTRY_IMPORT
           if d.dll.lower() == b"libxls.dll" for s in d.imports}
engine = Cs(CS_ARCH_X86, CS_MODE_32)
engine.detail = True
engine.skipdata = True
print("DLibXls.dll  SHA256", sha256((root / "DLibXls.dll").read_bytes()).hexdigest())
for section in wrapper.sections:
    if section.Characteristics & 0x20000000:
        for i in engine.disasm(section.get_data(), wrapper.OPTIONAL_HEADER.ImageBase + section.VirtualAddress):
            if i.mnemonic in ("call", "jmp") and i.operands[0].type == X86_OP_MEM:
                slot = i.operands[0].mem.disp
                if slot in imports:
                    print(f"VA {i.address:#010x}   {i.mnemonic} {i.op_str:<24}  libxls!{imports[slot]}")
for name, markers in {
    "DLabel.exe": ["GraphicsScene", "ItemPropertyBrowser", "openLabelSlot","chooseLocalExcelFile","loadExcelFile","updatePreviewExcelData","confirmLoadExcelFile","batchprintSelected", "addVariablePrintPage"],
    "DatabaseManager.dll": ["ExcelParser","workbookPr"],
}.items():
    data = (root / name).read_bytes()
    print(name," SHA256",sha256(data).hexdigest())
    for marker in markers:
        print(f"file offset {data.index(marker.encode()):#010x}   {marker}")
