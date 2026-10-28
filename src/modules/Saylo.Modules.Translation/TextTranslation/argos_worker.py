"""Local JSON-lines worker. No user text in arguments, diagnostics or persistent storage."""
import argparse
import contextlib
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import queue
import sys
import threading
import time

MAX_TEXT = 1000


def deny_network(event, args):
    if event in ("socket.connect", "socket.connect_ex", "socket.bind", "socket.getaddrinfo", "urllib.Request"):
        raise PermissionError("Network access is disabled in the translation worker")


def validate_request(request):
    if not isinstance(request, dict):
        raise ValueError("Expected a JSON object")
    if request.get("protocol") != 1 or not isinstance(request.get("id"), str) or not request["id"]:
        raise ValueError("Invalid protocol or correlation ID")
    if (request.get("source"), request.get("target")) not in (("es", "en"), ("en", "es")):
        raise ValueError("Unsupported language pair")
    if not isinstance(request.get("text"), str) or not request["text"].strip() or len(request["text"]) > MAX_TEXT:
        raise ValueError("Invalid text length")


def load_models(root, threads):
    if importlib.metadata.version("ctranslate2") != "4.8.4":
        raise RuntimeError("Unsupported CTranslate2 version")
    # Initialize numerical extensions before CTranslate2 creates native worker pools.
    import numpy
    import ctranslate2
    import sentencepiece
    from sacremoses import MosesTokenizer, MosesDetokenizer, MosesPunctNormalizer
    from subword_nmt.apply_bpe import BPE

    manifest = json.loads((root / "argos-models.json").read_text(encoding="utf-8"))
    if manifest["schemaVersion"] != 1:
        raise ValueError("Unsupported manifest")
    loaded = {}
    for item in manifest["models"]:
        pair = (item["source"], item["target"])
        if pair not in (("es", "en"), ("en", "es")) or pair in loaded:
            raise ValueError("Unexpected model pair")
        directory = (root / item["directory"]).resolve()
        if not directory.is_relative_to(root):
            raise ValueError("Model directory outside installation")
        declared = {entry["relativePath"] for entry in item["files"]}
        actual = {path.relative_to(directory).as_posix() for path in directory.rglob("*") if path.is_file()}
        if declared != actual:
            raise ValueError("Model directory differs from its integrity manifest")
        for entry in item["files"]:
            path = (directory / entry["relativePath"]).resolve()
            if not path.is_relative_to(directory) or path.stat().st_size != entry["sizeBytes"]:
                raise ValueError("Invalid model file")
            with path.open("rb") as stream:
                if hashlib.file_digest(stream, "sha256").hexdigest() != entry["sha256"]:
                    raise ValueError("Invalid model checksum")
        metadata = json.loads((directory / "metadata.json").read_text(encoding="utf-8"))
        if (metadata["from_code"], metadata["to_code"], metadata["package_version"]) != (*pair, item["version"]):
            raise ValueError("Model metadata mismatch")
        if (directory / "sentencepiece.model").exists():
            processor = sentencepiece.SentencePieceProcessor(model_file=str(directory / "sentencepiece.model"))
            encode = lambda text, processor=processor: processor.encode(text, out_type=str)
            decode = lambda tokens, processor=processor: processor.decode(tokens)
        else:
            tokenizer = MosesTokenizer(pair[0])
            detokenizer = MosesDetokenizer(pair[1])
            normalizer = MosesPunctNormalizer(pair[0])
            with (directory / "bpe.model").open(encoding="utf-8") as codes:
                bpe = BPE(codes)
            encode = lambda text, tokenizer=tokenizer, normalizer=normalizer, bpe=bpe: bpe.segment_tokens(
                tokenizer.tokenize(normalizer.normalize(text)))
            decode = lambda tokens, detokenizer=detokenizer: detokenizer.detokenize(
                " ".join(tokens).replace("@@ ", "").split(" "))
        translator = ctranslate2.Translator(str(directory / "model"), device="cpu", compute_type="int8",
                                           inter_threads=1, intra_threads=threads)
        loaded[pair] = (encode, decode, metadata.get("target_prefix", ""), translator)
    if set(loaded) != {("es", "en"), ("en", "es")}:
        raise ValueError("Both direct models are required")

    def translate(text, source, target):
        encode, decode, target_prefix, translator = loaded[(source, target)]
        translated = []
        for paragraph in text.split("\n"):
            if not paragraph.strip():
                translated.append("")
                continue
            tokens = encode(paragraph)
            prefix = [[target_prefix]] if target_prefix else None
            result = translator.translate_batch([tokens], target_prefix=prefix, replace_unknowns=True,
                                                beam_size=4, num_hypotheses=1, length_penalty=0.2,
                                                max_input_length=0, max_decoding_length=512)[0]
            if len(result.hypotheses[0]) >= 512:
                raise ValueError("Translation exceeded the output token limit")
            value = decode(result.hypotheses[0])
            if target_prefix and value.startswith(target_prefix):
                value = value[len(target_prefix):]
            translated.append(value.strip())
        return "\n".join(translated)
    return translate


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--models", type=Path, required=True)
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--threads", type=int, default=4)
    args = parser.parse_args()
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    # Keep an exclusive protocol handle; library stdout, including native writes, goes to stderr.
    protocol = os.fdopen(os.dup(sys.stdout.fileno()), "w", encoding="utf-8", buffering=1)
    os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
    sys.stdout = sys.stderr
    for name, value in {"ARGOS_DEBUG": "0", "ARGOS_DEVICE_TYPE": "cpu", "ARGOS_MODEL_PROVIDER": "OPENNMT",
                        "ARGOS_CHUNK_TYPE": "NONE", "XDG_DATA_HOME": str(args.data / "data"),
                        "XDG_CONFIG_HOME": str(args.data / "config"), "XDG_CACHE_HOME": str(args.data / "cache"),
                        "ARGOS_PACKAGES_DIR": str(args.models)}.items():
        os.environ[name] = value
    sys.addaudithook(deny_network)

    def send(payload):
        protocol.write(json.dumps({"protocol": 1, **payload}, ensure_ascii=True) + "\n")
        protocol.flush()

    # EOF means the parent exited or closed the pipe. Exit even during blocked native work.
    requests = queue.Queue(maxsize=1)
    def read_input():
        while True:
            line = sys.stdin.readline(32768)
            if not line:
                os._exit(0)
            if not line.endswith("\n"):
                os._exit(2)
            requests.put(line)
    started = time.perf_counter()
    try:
        translate = load_models(args.models.resolve(), args.threads)
        threading.Thread(target=read_input, daemon=True).start()
        send({"id": "ready", "ok": True, "pid": os.getpid(), "startupMs": (time.perf_counter() - started) * 1000})
    except (ValueError, FileNotFoundError):
        send({"id": "ready", "ok": False, "code": "model_invalid"})
        return 2
    except Exception:
        send({"id": "ready", "ok": False, "code": "initialization_failed"})
        return 2
    while True:
        request_id = "invalid"
        try:
            request = json.loads(requests.get())
            validate_request(request)
            request_id = request["id"]
            with contextlib.redirect_stdout(sys.stderr):
                text = translate(request["text"], request["source"], request["target"])
            if not text.strip():
                raise ValueError("Empty engine output")
            send({"id": request_id, "ok": True, "text": text})
        except Exception:
            # Never print exception messages or request bodies: either can contain user text.
            send({"id": request_id, "ok": False, "code": "translation_failed"})


if __name__ == "__main__":
    raise SystemExit(main())
