"""Compare authorized corpus evidence without modifying historical results or assigning linguistic scores."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def compare_records(cases, previous_rows, previous_reviews, current_rows):
    fixtures = {item["id"]: item for item in cases}
    previous = {item["Id"]: item for item in previous_rows}
    reviews = {item["id"]: item for item in previous_reviews}
    current = {item["Id"]: item for item in current_rows}
    if not (len(fixtures) == len(cases) == len(previous) == len(previous_rows) ==
            len(reviews) == len(previous_reviews) == len(current) == len(current_rows)):
        raise ValueError("Evidence IDs are duplicated or missing.")
    if not fixtures.keys() == previous.keys() == reviews.keys() == current.keys():
        raise ValueError("Evidence IDs do not match.")
    rows = []
    critical_alterations = 0
    literal_losses = 0
    for identifier, fixture in fixtures.items():
        old, review, new = previous[identifier], reviews[identifier], current[identifier]
        if (new["Input"] != fixture["text"] or old["Input"] != fixture["text"] or
                new["PreviousOutput"] != old["Output"] or review["observedOutput"] != old["Output"]):
            raise ValueError("Corpus or historical outputs do not match.")
        output = new["Output"]
        missing = []
        if output is not None:
            for literal in fixture["exactLiterals"]:
                pattern = r"(?<!\w)" + re.escape(literal) + r"(?!\w)"
                if len(re.findall(pattern, output)) != len(re.findall(pattern, fixture["text"])):
                    missing.append(literal)
        severity = review["maximumSeverity"]
        if severity == "critical" and missing:
            critical_alterations += 1
        literal_losses += bool(missing)
        if output is None:
            comparison = "controlled_rejection_not_a_corrected_translation"
        elif output == old["Output"]:
            comparison = "unchanged_previous_issue" if severity != "none_observed" else "unchanged_no_previous_issue"
        else:
            comparison = "changed_requires_new_linguistic_review"
        rows.append({
            "id": identifier, "source": fixture["source"], "input": fixture["text"],
            "previousOutput": old["Output"], "previousMaximumSeverity": severity,
            "newOutput": output, "status": new["Status"], "constraint": new.get("Constraint"),
            "comparison": comparison, "literalWarnings": missing,
            "humanBilingualReview": "pending"
        })
    major = [row for row in rows if row["previousMaximumSeverity"] == "major"]
    return {
        "schemaVersion": 1, "totalCases": len(rows),
        "acceptedByRules": sum(row["newOutput"] is not None for row in rows),
        "controlledRejections": sum(row["newOutput"] is None for row in rows),
        "criticalLiteralAlterationsAccepted": critical_alterations,
        "casesWithLiteralWarnings": literal_losses,
        "previousMajorCases": len(major),
        "previousMajorCasesUnavailable": sum(row["newOutput"] is None for row in major),
        "previousMajorCasesDeliveredForReview": sum(row["newOutput"] is not None for row in major),
        "linguisticAcceptance": "not_established",
        "enterpriseAcceptance": False,
        "note": "A blocked translation is not a corrected translation. Literal checks do not certify meaning.",
        "results": rows
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--new", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    dataset = root / "docs/validation/translation-quality-cases.json"
    previous = root / "docs/validation/results/2026-10-28/translation-quality.json"
    review = root / "docs/validation/translation-quality-review.json"
    load = lambda path: json.loads(path.read_text(encoding="utf-8-sig"))
    corpus, historical, old_review, current = load(dataset), load(previous), load(review), load(args.new)
    if current["DatasetSha256"] != digest(dataset) or current["HistoricalResultsSha256"] != digest(previous):
        raise ValueError("Evidence hashes do not match the frozen corpus.")
    result = compare_records(corpus["cases"], historical["Results"], old_review["results"], current["Results"])
    if result["totalCases"] != 80:
        raise ValueError("Expected the complete 80-case acceptance corpus.")
    result["evidenceHashes"] = {"dataset": digest(dataset), "historicalResults": digest(previous),
                                "historicalReview": digest(review), "currentResults": digest(args.new)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Compared {result['totalCases']} cases: {result['acceptedByRules']} rule acceptances, "
          f"{result['controlledRejections']} controlled rejections; human acceptance pending.")


if __name__ == "__main__":
    main()
