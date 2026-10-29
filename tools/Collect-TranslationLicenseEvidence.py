"""Collect installed artifact evidence; license approval remains a separate review."""
import argparse
import hashlib
import importlib.metadata as metadata
import json
from pathlib import Path
import platform
import sys
import urllib.request
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def evidence_file(path, label):
    return {"path": label, "sizeBytes": path.stat().st_size, "sha256": digest(path)}


def is_notice(path):
    return path.name.lower().split(".")[0] in {"license", "licence", "copying", "notice", "authors"}


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": "Saylo-License-Validation"})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read()
    return data


def collect(models, archives, online):
    distributions = []
    for dist in sorted(metadata.distributions(), key=lambda item: item.metadata["Name"].lower()):
        name, version = dist.metadata["Name"], dist.version
        notices, native, resources = [], [], []
        for entry in dist.files or []:
            path = Path(dist.locate_file(entry))
            if not path.is_file():
                continue
            label = str(entry).replace("\\", "/")
            if is_notice(path):
                notices.append(evidence_file(path, label))
            if path.suffix.lower() in {".dll", ".pyd"}:
                native.append(evidence_file(path, label))
            if name.lower() == "sacremoses" and "/data/" in label:
                resources.append(evidence_file(path, label))
        component = {
            "name": name, "version": version,
            "source": f"https://pypi.org/project/{name}/{version}/",
            "projectUrls": dist.metadata.get_all("Project-URL") or [dist.metadata.get("Home-page")],
            "declaredLicense": dist.metadata.get("License-Expression") or dist.metadata.get("License"),
            "licenseClassifiers": [value for value in dist.metadata.get_all("Classifier") or [] if value.startswith("License")],
            "requiresDist": dist.requires or [], "noticeFiles": notices, "nativeFiles": native,
            "embeddedResources": resources,
            "metadataEvidence": evidence_file(Path(dist._path) / "METADATA", f"{Path(dist._path).name}/METADATA"),
            "status": "evidence_collected_not_redistribution_approval"
        }
        if online:
            try:
                payload = fetch(f"https://pypi.org/pypi/{name}/{version}/json")
                info = json.loads(payload)
                component["registryEvidence"] = {
                    "url": f"https://pypi.org/pypi/{name}/{version}/json",
                    "responseSha256": hashlib.sha256(payload).hexdigest(),
                    "license": info["info"].get("license_expression") or info["info"].get("license"),
                    "compatibleWheelCandidates": [
                        {"filename": wheel["filename"], "sha256": wheel["digests"]["sha256"], "url": wheel["url"]}
                        for wheel in info["urls"] if wheel["filename"].endswith(".whl") and
                        ("none-any" in wheel["filename"] or ("win_amd64" in wheel["filename"] and
                         ("cp312" in wheel["filename"] or "abi3" in wheel["filename"])))
                    ],
                    "note": "Registry candidate hashes do not prove the exact installed wheel origin."
                }
            except Exception as failure:
                component["registryError"] = type(failure).__name__
        distributions.append(component)

    manifest_path = models / "argos-models.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    model_evidence = []
    for model in manifest["models"]:
        directory = models / model["directory"]
        entries = []
        for entry in model["files"]:
            path = directory / entry["relativePath"]
            actual = evidence_file(path, entry["relativePath"])
            actual["matchesManifest"] = actual["sha256"] == entry["sha256"] and actual["sizeBytes"] == entry["sizeBytes"]
            actual["licenseStatus"] = "package_specific_scope_and_provenance_pending"
            entries.append(actual)
        actual_paths = {path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_file()}
        declared_paths = {item["relativePath"] for item in model["files"]}
        archive_name = f"translate-{model['source']}_{model['target']}-{model['version'].replace('.', '_')}.argosmodel"
        archive = archives / archive_name
        package = {
            "id": model["id"], "version": model["version"], "source": "https://argos-net.com/v1/" + archive_name,
            "readme": (directory / "README.md").read_text(encoding="utf-8"),
            "metadata": json.loads((directory / "metadata.json").read_text(encoding="utf-8")),
            "files": entries, "unexpectedFiles": sorted(actual_paths - declared_paths),
            "missingFiles": sorted(declared_paths - actual_paths),
            "generalPublisherStatement": "https://github.com/argosopentech/argos-translate/issues/533",
            "redistributionApproved": False
        }
        if archive.exists():
            package["archiveEvidence"] = evidence_file(archive, archive_name)
            package["archiveMatchesPinnedHash"] = package["archiveEvidence"]["sha256"] == model["archiveSha256"]
            with zipfile.ZipFile(archive) as bundle:
                package["archiveNoticePaths"] = [entry for entry in bundle.namelist() if is_notice(Path(entry))]
                package["excludedStanzaEntries"] = len([entry for entry in bundle.namelist() if "/stanza/" in entry])
                metadata_entries = [entry for entry in bundle.namelist() if entry.count("/") == 1 and entry.endswith("/metadata.json")]
                if len(metadata_entries) != 1:
                    raise ValueError("Expected a single package root")
                prefix = metadata_entries[0].split("/")[0] + "/"
                for entry in entries:
                    with bundle.open(prefix + entry["path"]) as stream:
                        entry["matchesPinnedArchiveMember"] = hashlib.file_digest(stream, "sha256").hexdigest() == entry["sha256"]
        model_evidence.append(package)

    names = {item["name"].lower().replace("_", "-") for item in distributions}
    forbidden = {"minisbd", "argostranslate", "stanza", "torch", "spacy", "onnxruntime"}
    python_license = Path(sys.base_prefix) / "LICENSE.txt"
    result = {
        "schemaVersion": 1, "environment": {"python": platform.python_version(), "architecture": platform.machine()},
        "scope": "Installed reduced translation runtime, excluding unrelated evaluation venv and full application SBOM.",
        "pythonLicenseEvidence": evidence_file(python_license, "CPython/LICENSE.txt") if python_license.exists() else None,
        "packages": distributions, "forbiddenPackagesInstalled": sorted(names & forbidden),
        "modelsManifest": evidence_file(manifest_path, "argos-models.json"), "models": model_evidence,
        "redistributionApproved": False,
        "limitations": ["Versions alone are not a hash-locked supply chain.",
                       "Native transitive and statically linked components require upstream build provenance.",
                       "Tokenizer data and vocabulary rights are not established by software license alone.",
                       "pip and its vendored libraries are build tools; do not bundle them without a separate inventory."]
    }
    if online:
        sources = [
            "https://raw.githubusercontent.com/OpenNMT/CTranslate2/v4.8.4/LICENSE",
            "https://raw.githubusercontent.com/OpenNMT/CTranslate2/v4.8.4/.github/workflows/ci.yml",
            "https://raw.githubusercontent.com/OpenNMT/CTranslate2/v4.8.4/python/tools/prepare_build_environment_windows.sh",
            "https://raw.githubusercontent.com/google/sentencepiece/v0.2.2/LICENSE",
            "https://raw.githubusercontent.com/python/cpython/v3.12.14/LICENSE",
            "https://api.github.com/repos/argosopentech/argos-translate/issues/533/comments"
        ]
        result["onlineSources"] = []
        for url in sources:
            try:
                data = fetch(url)
                source = {"url": url, "sha256": hashlib.sha256(data).hexdigest(), "sizeBytes": len(data)}
                if url.endswith("/comments"):
                    comments = json.loads(data)
                    source["publisherStatements"] = [
                        {"author": item["user"]["login"], "createdAt": item["created_at"], "url": item["html_url"],
                         "statement": "The maintainer declares model binaries MIT/CC0; package-specific third-party scope remains unconfirmed."}
                        for item in comments if item["user"]["login"] == "PJ-Finlay" and "MIT/CC0" in item["body"]]
                result["onlineSources"].append(source)
            except Exception as failure:
                result["onlineSources"].append({"url": url, "error": type(failure).__name__})
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", type=Path, required=True)
    parser.add_argument("--archives", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--online", action="store_true", help="Explicitly fetch primary-source license metadata.")
    args = parser.parse_args()
    result = collect(args.models.resolve(), args.archives.resolve(), args.online)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Collected {len(result['packages'])} distributions and {len(result['models'])} models.")
    if result["forbiddenPackagesInstalled"]:
        raise SystemExit("Unexpected packages in the reduced runtime.")
