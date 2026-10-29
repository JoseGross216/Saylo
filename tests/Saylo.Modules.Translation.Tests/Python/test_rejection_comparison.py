import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("rejection_comparison", ROOT / "tools/Compare-TranslationRejections.py")
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class RejectionEvidenceTests(unittest.TestCase):
    def fixture(self, previous=None, output="Email fixture@example.com."):
        case = {"id": "es-test", "text": "Correo fixture@example.com.", "source": "es", "target": "en",
                "exactLiterals": ["fixture@example.com"]}
        old = {"id": case["id"], "source": "es", "input": case["text"], "newOutput": previous}
        new = {"Id": case["id"], "Source": "es", "Target": "en", "Input": case["text"],
               "PreviousOutput": previous, "Output": output,
               "Status": "accepted_by_rules" if output is not None else "controlled_integrity_rejection"}
        return case, old, new

    def test_recovery_does_not_claim_human_or_enterprise_acceptance(self):
        case, old, new = self.fixture()
        result = comparison.compare([case], [old], [new], {case["id"]})
        self.assertEqual([case["id"]], result["recovered"])
        self.assertEqual(0, result["criticalLiteralAlterationsAccepted"])
        self.assertFalse(result["enterpriseAcceptance"])
        self.assertEqual("not_established", result["linguisticAcceptance"])

    def test_rejection_is_not_a_corrected_translation(self):
        case, old, new = self.fixture(output=None)
        result = comparison.compare([case], [old], [new], {case["id"]})
        self.assertEqual([], result["recovered"])
        self.assertEqual([case["id"]], result["maintainedRejections"])
        self.assertEqual(0, result["acceptedByRules"])

    def test_silently_changed_critical_identifier_is_reported(self):
        case, old, new = self.fixture(output="Email changed@example.com.")
        self.assertEqual(1, comparison.compare([case], [old], [new], {case["id"]})["criticalLiteralAlterationsAccepted"])

    def test_evidence_corruption_is_rejected(self):
        for field, value in (("Input", "Changed input"), ("PreviousOutput", "Forged history"),
                             ("Source", "en"), ("Target", "es"), ("Status", "controlled_integrity_rejection")):
            case, old, new = self.fixture()
            new[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                comparison.compare([case], [old], [new], set())
        case, old, new = self.fixture()
        for cases, previous, current in (([case, case], [old], [new]), ([case], [], [new]), ([case], [old], [new, new])):
            with self.assertRaises(ValueError):
                comparison.compare(cases, previous, current, set())


if __name__ == "__main__":
    unittest.main()
