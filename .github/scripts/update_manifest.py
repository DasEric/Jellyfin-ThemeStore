import json
import os
from pathlib import Path

path = Path("manifest.json")
manifest = json.loads(path.read_text(encoding="utf-8"))
plugin = manifest[0]
plugin["imageUrl"] = os.environ["IMAGE_URL"]
version = {
    "version": os.environ["VERSION"],
    "changelog": "See the linked GitHub release notes.",
    "targetAbi": "10.11.0.0",
    "sourceUrl": os.environ["SOURCE_URL"],
    "checksum": os.environ["CHECKSUM"],
    "timestamp": os.environ["TIMESTAMP"],
    "dependencies": ["5e87cc92-571a-4d8d-8d98-d2d4147f9f90"],
}
plugin["versions"] = [item for item in plugin.get("versions", []) if item.get("version") != version["version"]]
plugin["versions"].insert(0, version)
path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
