"""Per-tool JSON data files (internal): the storage half of script.store_data.

Pure stdlib so it tests anywhere. Document-scoped files reuse the memory
module's identity rule: lowercased full path, stem + sha1[:8].
"""
import hashlib
import json
import os
import re


def path_for(root, tool_key, doc_path=None, suffix='json'):
    if doc_path is None:
        return os.path.join(root, '%s.%s' % (tool_key, suffix))
    lowered = str(doc_path).lower()
    stem = re.sub(r'[^a-z0-9]+', '_', os.path.splitext(os.path.basename(lowered))[0]).strip('_') or 'untitled'
    digest = hashlib.sha1(lowered.encode('utf-8')).hexdigest()[:8]
    return os.path.join(root, '%s-%s-%s.%s' % (tool_key, stem, digest, suffix))


def load_all(path):
    """The whole JSON object in the file; missing or corrupt yields {}."""
    try:
        with open(path, 'r') as f:
            data = json.load(f)
    except Exception:
        return {}
    return data if isinstance(data, dict) else {}


def _keep_aside_if_corrupt(path):
    """store() rewrites the whole file, so a file that will not parse would be
    replaced by a near-empty one and every other key lost for good. Keep the
    old bytes as <path>.corrupt (latest wins) before that happens."""
    try:
        with open(path, 'r') as f:
            text = f.read()
    except Exception:
        return
    if not text.strip():
        return
    try:
        if isinstance(json.loads(text), dict):
            return
    except Exception:
        pass
    from pynavis import _atomic
    _atomic.write_text(path + '.corrupt', text)


def store(path, key, value):
    from pynavis import _atomic
    folder = os.path.dirname(path)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    _keep_aside_if_corrupt(path)
    data = load_all(path)
    data[str(key)] = value
    _atomic.write_text(path, json.dumps(data, indent=2, sort_keys=True))
