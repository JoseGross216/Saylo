import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location("ranked_worker", ROOT / "src/modules/Saylo.Modules.Translation/TextTranslation/argos_worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class RankedCandidateTests(unittest.TestCase):
    def request(self):
        return {"protocol": 1, "id": "fixture", "text": "Authorized fixture.", "source": "es", "target": "en"}

    def test_existing_requests_without_candidate_field_remain_valid(self):
        worker.validate_request(self.request())

    def test_candidate_budget_accepts_only_bounded_exact_integers(self):
        for count in (1, 2, 3, 4):
            request = self.request()
            request["candidates"] = count
            worker.validate_request(request)
        for count in (None, True, False, "4", 1.5, 0, -1, 5, 1000000):
            with self.subTest(candidate_count=count):
                request = self.request()
                request["candidates"] = count
                with self.assertRaises(ValueError):
                    worker.validate_request(request)

    def test_ranked_requests_cannot_silently_return_only_one_paragraph(self):
        for text in ("one\ntwo", "one\rtwo", "one\r\ntwo"):
            request = self.request()
            request.update(candidates=4, text=text)
            with self.assertRaises(ValueError):
                worker.validate_request(request)

    def test_primary_protocol_still_supports_multiple_paragraphs(self):
        request = self.request()
        request["text"] = "one\ntwo"
        worker.validate_request(request)

    def test_complete_candidates_keep_rank_and_are_deduplicated(self):
        self.assertEqual(["one", "two", "three"],
                         worker.decode_candidates([["one"], ["two"], ["two"], ["three"]], lambda tokens: " ".join(tokens)))

    def test_truncated_hypotheses_are_rejected_without_returning_partial_tokens(self):
        self.assertEqual(["complete"], worker.decode_candidates([["x"] * 512, ["complete"]], lambda tokens: " ".join(tokens)))
        with self.assertRaises(ValueError):
            worker.decode_candidates([["x"] * 512], lambda tokens: " ".join(tokens))

    def test_oversized_or_empty_decoded_candidates_are_not_clipped(self):
        self.assertEqual(["complete"], worker.decode_candidates([["big"], ["complete"]],
                         lambda tokens: "x" * 4097 if tokens == ["big"] else "complete"))
        with self.assertRaises(ValueError):
            worker.decode_candidates([["empty"]], lambda tokens: "")

    def test_hypothesis_count_is_bounded_independently_of_request_validation(self):
        for hypotheses in ([], [["fixture"]] * 5):
            with self.assertRaises(ValueError):
                worker.decode_candidates(hypotheses, lambda tokens: " ".join(tokens))

    def test_target_prefix_is_removed_only_from_the_start(self):
        self.assertEqual(["translated"], worker.decode_candidates([["PREFIXtranslated"]], lambda tokens: tokens[0], "PREFIX"))
        self.assertEqual(["valuePREFIX"], worker.decode_candidates([["valuePREFIX"]], lambda tokens: tokens[0], "PREFIX"))


if __name__ == "__main__":
    unittest.main()
