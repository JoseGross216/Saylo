import copy
import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[3] / "tools/Compare-TranslationIntegrity.py"
spec = importlib.util.spec_from_file_location("comparison", SCRIPT)
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class IntegrityEvidenceTests(unittest.TestCase):
    def fixtures(self):
        cases = [{"id": "fixture", "source": "es", "text": "Email soporte@example.com.", "exactLiterals": ["soporte@example.com"]}]
        old = [{"Id": "fixture", "Input": cases[0]["text"], "Output": "Email support@example.com."}]
        reviews = [{"id": "fixture", "observedOutput": old[0]["Output"], "maximumSeverity": "critical"}]
        current = [{"Id": "fixture", "Input": cases[0]["text"], "PreviousOutput": old[0]["Output"],
                    "Output": "Email soporte@example.com.", "Status": "accepted_by_rules"}]
        return cases, old, reviews, current

    def test_real_corrected_literal_is_distinct_from_linguistic_acceptance(self):
        result = comparison.compare_records(*self.fixtures())
        self.assertEqual(0, result["criticalLiteralAlterationsAccepted"])
        self.assertFalse(result["enterpriseAcceptance"])
        self.assertEqual("changed_requires_new_linguistic_review", result["results"][0]["comparison"])

    def test_silently_changed_literal_is_counted_as_a_critical_violation(self):
        cases, old, reviews, current = self.fixtures()
        current[0]["Output"] = "Email support@example.com."
        result = comparison.compare_records(cases, old, reviews, current)
        self.assertEqual(1, result["criticalLiteralAlterationsAccepted"])

    def test_blocked_major_case_is_not_counted_as_a_corrected_translation(self):
        cases, old, reviews, current = self.fixtures()
        reviews[0]["maximumSeverity"] = "major"
        current[0]["Output"] = None
        current[0]["Status"] = "controlled_integrity_rejection"
        result = comparison.compare_records(cases, old, reviews, current)
        self.assertEqual(1, result["previousMajorCasesUnavailable"])
        self.assertEqual(0, result["acceptedByRules"])
        self.assertFalse(result["enterpriseAcceptance"])

    def test_mismatched_history_is_rejected_without_exposing_text(self):
        cases, old, reviews, current = self.fixtures()
        current[0]["PreviousOutput"] = "private text must not appear"
        with self.assertRaisesRegex(ValueError, "^Corpus or historical outputs do not match.$"):
            comparison.compare_records(cases, old, reviews, current)

    def test_duplicate_or_missing_ids_are_rejected(self):
        cases, old, reviews, current = self.fixtures()
        current.append(copy.deepcopy(current[0]))
        with self.assertRaises(ValueError):
            comparison.compare_records(cases, old, reviews, current)


if __name__ == "__main__":
    unittest.main()
