"""Compare the frozen 80-case corpus with Tarea 3.2 without assigning semantic approval."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare(cases, previous_rows, current_rows, critical_ids):
    def index(rows, key):
        values = {row[key]: row for row in rows}
        if len(values) != len(rows):
            raise ValueError("Duplicate evidence IDs.")
        return values

    fixtures = index(cases, "id")
    previous = index(previous_rows, "id")
    current = index(current_rows, "Id")
    if fixtures.keys() != previous.keys() or fixtures.keys() != current.keys():
        raise ValueError("Missing or unexpected evidence IDs.")
    rows = []
    for identifier, fixture in fixtures.items():
        old, new = previous[identifier], current[identifier]
        if (old["input"] != fixture["text"] or new["Input"] != fixture["text"] or
                old["source"] != fixture["source"] or new["Source"] != fixture["source"] or
                new["Target"] != fixture["target"] or new["PreviousOutput"] != old["newOutput"]):
            raise ValueError("Corpus or historical output mismatch.")
        output = new["Output"]
        expected_status = "controlled_integrity_rejection" if output is None else "accepted_by_rules"
        if new["Status"] != expected_status or output is not None and (not isinstance(output, str) or not output.strip()):
            raise ValueError("Inconsistent output status.")
        if old["newOutput"] is None:
            change = "maintained_rejection" if output is None else "recovered_requires_human_review"
        else:
            change = "new_controlled_rejection" if output is None else (
                "unchanged" if output == old["newOutput"] else "changed_requires_human_review")
        warnings = []
        if output is not None:
            for literal in fixture["exactLiterals"]:
                pattern = r"(?<!\w)" + re.escape(literal) + r"(?!\w)"
                if len(re.findall(pattern, output)) != len(re.findall(pattern, fixture["text"])):
                    warnings.append(literal)
        rows.append({"id": identifier, "source": fixture["source"], "input": fixture["text"],
                     "previousOutput": old["newOutput"], "newOutput": output, "constraint": new.get("Constraint"),
                     "change": change, "literalWarnings": warnings,
                     "criticalLiteralAlterationAccepted": identifier in critical_ids and bool(warnings),
                     "humanBilingualReview": "pending"})
    select = lambda change: [row["id"] for row in rows if row["change"] == change]
    return {"schemaVersion": 1, "totalCases": len(rows),
            "acceptedByRules": sum(row["newOutput"] is not None for row in rows),
            "controlledRejections": sum(row["newOutput"] is None for row in rows),
            "recovered": select("recovered_requires_human_review"),
            "maintainedRejections": select("maintained_rejection"),
            "newRejections": select("new_controlled_rejection"),
            "changedDeliveredOutputs": select("changed_requires_human_review"),
            "criticalLiteralAlterationsAccepted": sum(row["criticalLiteralAlterationAccepted"] for row in rows),
            "enterpriseAcceptance": False, "linguisticAcceptance": "not_established",
            "note": "Lexical warnings include equivalent hour formats; they are not automatic semantic failures. New rejections reduce availability.",
            "results": rows}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--new", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    dataset = root / "docs/validation/translation-quality-cases.json"
    previous = root / "docs/validation/integrity-results/2026-10-28/integrity-comparison.json"
    review = root / "docs/validation/translation-quality-review.json"
    load = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
    current = load(args.new)
    if current["DatasetSha256"] != digest(dataset) or current["PreviousComparisonSha256"] != digest(previous):
        raise ValueError("Frozen evidence hashes do not match.")
    critical = {row["id"] for row in load(review)["results"] if row["maximumSeverity"] == "critical"}
    result = compare(load(dataset)["cases"], load(previous)["results"], current["Results"], critical)
    if result["totalCases"] != 80:
        raise ValueError("Expected the complete unmodified 80-case corpus.")
    result["profile"] = current["Profile"]
    result["evidenceHashes"] = {"dataset": digest(dataset), "previousComparison": digest(previous),
                                "historicalReview": digest(review), "currentResults": digest(args.new)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{result['acceptedByRules']} accepted by rules; {result['controlledRejections']} blocked; "
          f"{len(result['recovered'])} recovered; {len(result['newRejections'])} new rejections; human review pending.")
    if result["criticalLiteralAlterationsAccepted"]:
        raise SystemExit("Critical literal alteration detected.")


if __name__ == "__main__":
    main()
