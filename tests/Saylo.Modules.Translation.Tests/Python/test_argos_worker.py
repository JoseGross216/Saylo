import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[3]
WORKER = ROOT / "src/modules/Saylo.Modules.Translation/TextTranslation/argos_worker.py"
spec = importlib.util.spec_from_file_location("saylo_worker", WORKER)
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class WorkerProtocolTests(unittest.TestCase):
    def request(self):
        return {"protocol": 1, "id": "fixture", "text": "fixture", "source": "es", "target": "en"}

    def test_both_direct_pairs_are_valid(self):
        for source, target in (("es", "en"), ("en", "es")):
            request = self.request()
            request.update(source=source, target=target)
            worker.validate_request(request)

    def test_empty_and_long_text_are_rejected(self):
        for text in (None, "", " \t", "a" * 1001):
            with self.subTest(text_length=len(text) if text else 0):
                request = self.request()
                request["text"] = text
                with self.assertRaises(ValueError):
                    worker.validate_request(request)

    def test_unsupported_or_equal_languages_are_rejected(self):
        for source, target in (("es", "es"), ("fr", "en"), ("en", None)):
            request = self.request()
            request.update(source=source, target=target)
            with self.assertRaises(ValueError):
                worker.validate_request(request)

    def test_protocol_and_correlation_are_required(self):
        for field, value in (("protocol", 2), ("id", None), ("id", "")):
            request = self.request()
            request[field] = value
            with self.assertRaises(ValueError):
                worker.validate_request(request)

    def test_non_object_payload_is_rejected(self):
        with self.assertRaises(ValueError):
            worker.validate_request([])

    def test_network_policy_rejects_outbound_and_dns_events(self):
        for event in ("socket.connect", "socket.connect_ex", "socket.bind", "socket.getaddrinfo", "urllib.Request"):
            with self.assertRaises(PermissionError):
                worker.deny_network(event, ())
        worker.deny_network("open", ())

    def test_installed_audit_hook_blocks_socket_before_connect(self):
        code = f"""
import importlib.util,sys,socket
spec=importlib.util.spec_from_file_location('worker',{str(WORKER)!r})
worker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)
sys.addaudithook(worker.deny_network)
try:
    socket.create_connection(('127.0.0.1',9),timeout=1)
except PermissionError:
    sys.exit(0)
sys.exit(1)
"""
        result = subprocess.run([sys.executable, "-I", "-B", "-c", code], capture_output=True, timeout=5)
        self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))


if __name__ == "__main__":
    unittest.main()
