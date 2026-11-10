"""Regenerate the offline LP2 vectors using the pinned PyroGraph source."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
upstream = root / "external" / "pyrograph"
commit = subprocess.check_output(["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True).strip()
if commit != "306f872a49fc347f04396bc2a4be912e6157a814":
    raise SystemExit(f"Unexpected PyroGraph commit: {commit}")
if subprocess.check_output(["git", "-C", str(upstream), "status", "--porcelain"], text=True).strip():
    raise SystemExit("PyroGraph source has local changes.")
package = upstream / "packages" / "laserpecker"
spec = importlib.util.spec_from_file_location("pyrograph_protocol", package / "src" / "laserpecker" / "protocol.py")
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)

commands = {
    "status": module.query(module.Query.STATUS),
    "version": module.query(module.Query.VERSION),
    "mac": module.query(module.Query.MAC),
    "list": module.file_list(2),
    "cached": module.query_file_cached(0xDEADBEEF, 2),
    "fields": module.build_frame(0x06, [(0xFF01, 1), (-2, 2), (0x01234567, 4)]),
    "max": module.build_frame(0x01, [(255, 1)] * module.MAX_DATA_LEN),
}
payload = bytes([0x33, 0xAA, 0xBB, 0x08, 0xCC])
long_reply = b"\xaa\xcc" + (len(payload) + 2).to_bytes(4, "big") + payload + sum(payload).to_bytes(2, "big")
assert module.long_frame_complete(long_reply)
captured = json.loads((package / "tests" / "fixtures_lp2.json").read_text(encoding="utf-8"))
assert all(module.frame_complete(bytes.fromhex(frame)) for frame in captured.values())
captured["long_synthetic"] = long_reply.hex()
vectors = {"commit": commit, "commands": {name: data.hex() for name, data in commands.items()}, "replies": captured}
Path(__file__).with_name("pyrograph-vectors.json").write_text(json.dumps(vectors, indent=2) + "\n", encoding="utf-8")

