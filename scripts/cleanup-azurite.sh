#!/usr/bin/env bash
set -euo pipefail

# Cleans up test containers and leftover integration-test blobs from local Azurite.
# Usage:
#   ./scripts/cleanup-azurite.sh            # Delete orphaned test-* containers and known integration-test blobs
#   ./scripts/cleanup-azurite.sh --dry-run  # Preview what would be deleted without modifying Azurite
#   ./scripts/cleanup-azurite.sh --all      # Delete test-* containers and wipe schemas/draft-schemas/payloads/processed

CONN_STR="${AZURE_STORAGE_CONNECTION_STRING:-DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;}"

DRY_RUN=0
WIPE_ALL=0

for arg in "$@"; do
    case "$arg" in
        --dry-run) DRY_RUN=1 ;;
        --all) WIPE_ALL=1 ;;
        -h|--help)
            sed -n '3,7p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown option: $arg" >&2
            exit 1
            ;;
    esac
done

python3 - "$CONN_STR" "$DRY_RUN" "$WIPE_ALL" <<'PY'
import sys, re, json, base64, hmac, hashlib, datetime, urllib.request, urllib.error, urllib.parse
import xml.etree.ElementTree as ET

conn_str = sys.argv[1]
dry_run = sys.argv[2] == "1"
wipe_all = sys.argv[3] == "1"

parts = dict(item.split("=", 1) for item in conn_str.strip(";").split(";") if "=" in item)
account_name = parts.get("AccountName", "devstoreaccount1")
account_key = base64.b64decode(parts.get("AccountKey", "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw=="))
blob_endpoint = parts.get("BlobEndpoint", f"http://127.0.0.1:10000/{account_name}").rstrip("/")
parsed_ep = urllib.parse.urlparse(blob_endpoint)
endpoint_prefix = parsed_ep.path.rstrip("/")

TEST_CONTAINER_RE = re.compile(r"^test-[0-9a-f]{12}-(schemas|draft-schemas|payloads|processed)$")
TEST_SCHEMA_RE = re.compile(
    r"^(integration_schema|integration_test|test_route_schema|edc-sites|edc_payload|edc_elements|edc_laestab|"
    r"yaml_hdr|draft_evt|draft_meta|draft_no_id|draft_event_schema|dup_schema|e2e_route|"
    r"finalised_guard|sched_empty|sched_invalid|sched_retire|sched_bad_ptr|sched_opt_id|"
    r"ui_journey|ui_tests)(_|-|$)"
)
APP_CONTAINERS = ("schemas", "draft-schemas", "payloads", "processed")

def storage_request(method, path, query=""):
    now = datetime.datetime.now(datetime.timezone.utc).strftime("%a, %d %b %Y %H:%M:%S GMT")
    headers = {
        "x-ms-date": now,
        "x-ms-version": "2021-08-06",
    }
    canon_headers = "".join(f"{k}:{headers[k]}\n" for k in sorted(headers))
    url_path = f"{endpoint_prefix}/{path.lstrip('/')}" if path else f"{endpoint_prefix}"
    # Azure SharedKey CanonicalizedResource for path-style URLs prepends /{account_name} before the request URI path
    if endpoint_prefix.lstrip("/") == account_name:
        canon_resource = f"/{account_name}{url_path}"
    else:
        canon_resource = f"/{account_name}/{path.lstrip('/')}"
    if query:
        for kv in sorted(query.split("&")):
            k, _, v = kv.partition("=")
            canon_resource += f"\n{k.lower()}:{urllib.parse.unquote(v)}"

    s2s = "\n".join([method, "", "", "", "", "", "", "", "", "", "", "", canon_headers + canon_resource])
    sig = base64.b64encode(hmac.new(account_key, s2s.encode("utf-8"), hashlib.sha256).digest()).decode("utf-8")
    headers["Authorization"] = f"SharedKey {account_name}:{sig}"

    full_url = f"{blob_endpoint}/{path.lstrip('/')}" if path else blob_endpoint
    if query:
        full_url = f"{full_url}?{query}"

    req = urllib.request.Request(full_url, headers=headers, method=method)
    with urllib.request.urlopen(req) as resp:
        return resp.status, resp.read()

def list_containers():
    containers = []
    marker = ""
    while True:
        q = "comp=list" + (f"&marker={urllib.parse.quote(marker)}" if marker else "")
        _, body = storage_request("GET", "", q)
        root = ET.fromstring(body)
        containers.extend(c.findtext("Name") for c in root.findall(".//Container") if c.findtext("Name"))
        marker = root.findtext("NextMarker") or ""
        if not marker:
            break
    return containers

def list_blobs(container):
    blobs = []
    marker = ""
    while True:
        q = "restype=container&comp=list" + (f"&marker={urllib.parse.quote(marker)}" if marker else "")
        _, body = storage_request("GET", container, q)
        root = ET.fromstring(body)
        blobs.extend(b.findtext("Name") for b in root.findall(".//Blob") if b.findtext("Name"))
        marker = root.findtext("NextMarker") or ""
        if not marker:
            break
    return blobs

def delete_blob(container, blob_name):
    if dry_run:
        print(f"  [dry-run] delete blob {container}/{blob_name}")
        return
    encoded = urllib.parse.quote(blob_name, safe="/")
    storage_request("DELETE", f"{container}/{encoded}")

def delete_container(container):
    if dry_run:
        print(f"  [dry-run] delete container {container}")
        return
    storage_request("DELETE", container, "restype=container")

def ensure_container(container):
    if dry_run:
        return
    try:
        storage_request("PUT", container, "restype=container")
    except urllib.error.HTTPError as e:
        if e.code != 409:
            raise

try:
    containers = list_containers()
except Exception as ex:
    print(f"[ERROR] Failed to connect to Azurite at {blob_endpoint}: {ex}", file=sys.stderr)
    sys.exit(1)

deleted_containers = 0
deleted_blobs = 0

for c in containers:
    if TEST_CONTAINER_RE.match(c):
        delete_container(c)
        deleted_containers += 1

if wipe_all:
    for c in APP_CONTAINERS:
        if c in containers:
            blobs = list_blobs(c)
            for b in blobs:
                delete_blob(c, b)
                deleted_blobs += 1
            ensure_container(c)
else:
    for c in ("schemas", "draft-schemas"):
        if c not in containers:
            continue
        for b in list_blobs(c):
            top = b.split("/")[0]
            if TEST_SCHEMA_RE.match(top):
                delete_blob(c, b)
                deleted_blobs += 1

    if "processed" in containers:
        for b in list_blobs("processed"):
            schema_match = re.search(r"schema=([^/]+)", b)
            if schema_match and TEST_SCHEMA_RE.match(schema_match.group(1)):
                delete_blob("processed", b)
                deleted_blobs += 1

    if "payloads" in containers:
        blobs = list_blobs("payloads")
        test_sessions = set()
        for b in blobs:
            if b.endswith("/session.json"):
                _, raw = storage_request("GET", f"payloads/{urllib.parse.quote(b, safe='/')}")
                try:
                    data = json.loads(raw.decode("utf-8"))
                    schema_name = data.get("SchemaName") or data.get("schemaName") or ""
                    if TEST_SCHEMA_RE.match(schema_name):
                        test_sessions.add(b.split("/")[0])
                except Exception:
                    pass
        for b in blobs:
            if b.split("/")[0] in test_sessions:
                delete_blob("payloads", b)
                deleted_blobs += 1

mode = "DRY-RUN" if dry_run else ("WIPE-ALL" if wipe_all else "CLEANED")
print(f"[{mode}] Removed {deleted_containers} test container(s) and {deleted_blobs} test blob(s) from {blob_endpoint}.")
PY
