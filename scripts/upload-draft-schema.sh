#!/usr/bin/env bash
set -euo pipefail

# Helper script to test automatic [BlobTrigger] draft-schemas ingestion against local Azurite (127.0.0.1:10000).
# Usage:
#   ./scripts/upload-draft-schema.sh                                     # Uploads all 3 CDE ODCS YAML contracts
#   ./scripts/upload-draft-schema.sh path/to/file.odcs.yaml [blob-path]  # Uploads a specific file (.odcs.yaml, .schema.json, .rules.json)

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONN_STR="${AZURE_STORAGE_CONNECTION_STRING:-DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;}"
DRAFT_CONTAINER="${DRAFT_CONTAINER:-draft-schemas}"
SCHEMAS_CONTAINER="${SCHEMAS_CONTAINER:-schemas}"

python3 - "$ROOT_DIR" "$CONN_STR" "$DRAFT_CONTAINER" "$SCHEMAS_CONTAINER" "$@" <<'PY'
import sys, os, base64, hmac, hashlib, datetime, urllib.request, urllib.error

root_dir = sys.argv[1]
conn_str = sys.argv[2]
draft_container = sys.argv[3]
schemas_container = sys.argv[4]
args = sys.argv[5:]

parts = dict(item.split("=", 1) for item in conn_str.strip(";").split(";") if "=" in item)
account_name = parts.get("AccountName", "devstoreaccount1")
account_key = base64.b64decode(parts.get("AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="))
blob_endpoint = parts.get("BlobEndpoint", f"http://127.0.0.1:10000/{account_name}").rstrip("/")

def storage_request(method, path, body=b"", content_type="application/octet-stream", extra_headers=None):
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%a, %d %b %Y %H:%M:%S GMT")
    version = "2021-08-06"
    headers = {
        "x-ms-date": now,
        "x-ms-version": version,
    }
    if method == "PUT" and "restype=container" not in path:
        headers["x-ms-blob-type"] = "BlockBlob"
        headers["Content-Type"] = content_type
        headers["Content-Length"] = str(len(body))
    if extra_headers:
        headers.update(extra_headers)

    canon_headers = "".join(f"{k.lower()}:{headers[k]}\n" for k in sorted(headers) if k.lower().startswith("x-ms-"))
    path_only, _, query = path.partition("?")
    canon_resource = f"/{account_name}/{path_only.lstrip('/')}"
    if query:
        for kv in sorted(query.split("&")):
            k, _, v = kv.partition("=")
            canon_resource += f"\n{k.lower()}:{v}"

    content_len = str(len(body)) if (method == "PUT" and len(body) > 0) else ""
    ct = headers.get("Content-Type", "")
    string_to_sign = "\n".join([
        method, "", "", content_len, "", ct, "", "", "", "", "", "",
        canon_headers + canon_resource
    ])
    sig = base64.b64encode(hmac.new(account_key, string_to_sign.encode("utf-8"), hashlib.sha256).digest()).decode("utf-8")
    headers["Authorization"] = f"SharedKey {account_name}:{sig}"

    req = urllib.request.Request(f"{blob_endpoint}/{path.lstrip('/')}", data=body if method == "PUT" else None, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read()
    except urllib.error.HTTPError as e:
        if e.code == 409 and "restype=container" in path:
            return 200, b""
        raise

for container in (draft_container, schemas_container, "payloads", "processed"):
    storage_request("PUT", f"{container}?restype=container")

if len(args) >= 1:
    local_file = os.path.abspath(args[0])
    blob_name = args[1] if len(args) >= 2 else os.path.basename(local_file)
    files_to_upload = [(local_file, blob_name)]
else:
    files_to_upload = [
        (os.path.join(root_dir, "cde-data-contracts/contracts/edc-school-site-jsonlogic.odcs.yaml"), "edc-school-site/v1.odcs.yaml"),
        (os.path.join(root_dir, "cde-data-contracts/contracts/edc-survey-elements.odcs.yaml"), "edc-survey-elements/v1.odcs.yaml"),
        (os.path.join(root_dir, "cde-data-contracts/contracts/edc-survey-evidenceandanswers.odcs.yaml"), "edc-survey-answers-evidence/v1.odcs.yaml"),
    ]

for local_path, blob_path in files_to_upload:
    with open(local_path, "rb") as f:
        data = f.read()
    ct = "application/x-yaml" if blob_path.endswith((".yaml", ".yml")) else "application/json"
    status, _ = storage_request("PUT", f"{draft_container}/{blob_path}", body=data, content_type=ct)
    print(f"[OK {status}] Uploaded {os.path.relpath(local_path, root_dir)} -> {draft_container}/{blob_path}")
PY
