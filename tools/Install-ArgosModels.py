"""Explicit development installation; never called by the application."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile

PACKAGES = [
    ("es", "en", "1.9", "translate-es_en-1_9.argosmodel", "c54df2b62fceaf54a3ce5d97db6bf56efd7940063329f6778f4212d2acb370d4"),
    ("en", "es", "1.0", "translate-en_es-1_0.argosmodel", "d698d0ef87ad70d5d184b7fa6965905bf4368f09a2bb9ffb165a79bac96af0c4"),
]


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def install(destination, archives, download):
    destination.mkdir(parents=True, exist_ok=True)
    archives.mkdir(parents=True, exist_ok=True)
    models = []
    for source, target, version, filename, expected in PACKAGES:
        archive = archives / filename
        if not archive.exists():
            if not download:
                raise FileNotFoundError(f"Provide {filename} or explicitly pass --download")
            url = "https://argos-net.com/v1/" + filename
            print(f"Downloading explicitly: {url}", flush=True)
            temporary = archive.with_suffix(".partial")
            urllib.request.urlretrieve(url, temporary)
            temporary.replace(archive)
        if sha256(archive) != expected:
            raise ValueError(f"Archive checksum mismatch: {filename}")
        model_dir = destination / f"{source}-{target}"
        if model_dir.exists():
            raise FileExistsError(f"Use a fresh installation directory: {model_dir}")
        with zipfile.ZipFile(archive) as package:
            metadata_names = [n for n in package.namelist() if n.count("/") == 1 and n.endswith("/metadata.json")]
            if len(metadata_names) != 1:
                raise ValueError("Expected one package root")
            prefix = metadata_names[0].split("/")[0] + "/"
            metadata = json.loads(package.read(metadata_names[0]))
            if (metadata["from_code"], metadata["to_code"], metadata["package_version"]) != (source, target, version):
                raise ValueError("Unexpected package metadata")
            for entry in package.infolist():
                if not entry.filename.startswith(prefix):
                    raise ValueError("Unexpected archive root")
                relative = entry.filename[len(prefix):]
                if not relative or entry.is_dir():
                    continue
                # The short-passage adapter does not execute or distribute Stanza models.
                if relative.startswith("stanza/"):
                    continue
                path = (model_dir / relative).resolve()
                if not path.is_relative_to(model_dir.resolve()) or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                    raise ValueError("Unsafe archive entry")
                if entry.file_size > 1_000_000_000:
                    raise ValueError("Unexpected model file size")
                path.parent.mkdir(parents=True, exist_ok=True)
                with package.open(entry) as stream, path.open("wb") as output:
                    shutil.copyfileobj(stream, output)
        files = [{"relativePath": str(path.relative_to(model_dir)).replace("\\", "/"),
                  "sizeBytes": path.stat().st_size, "sha256": sha256(path)}
                 for path in sorted(model_dir.rglob("*")) if path.is_file()]
        models.append({"id": f"argos-{source}-{target}", "version": version,
                       "source": source, "target": target, "directory": f"{source}-{target}",
                       "archiveSha256": expected, "files": files})
        print(f"Installed {source}->{target}, version {version}: {model_dir}", flush=True)
    manifest = destination / "argos-models.json"
    manifest.write_text(json.dumps({"schemaVersion": 1, "models": models}, indent=2), encoding="utf-8")
    print(f"Integrity manifest: {manifest}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--archives", type=Path, required=True)
    parser.add_argument("--download", action="store_true")
    args = parser.parse_args()
    install(args.destination.resolve(), args.archives.resolve(), args.download)
