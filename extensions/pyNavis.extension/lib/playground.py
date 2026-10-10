"""The Playground browser's logic, with no window and no network.

pyNavis Playground is a separate extension of experimental tools, published as
one zip plus a catalog.json that describes every tool in it (schema 1). The
Playground button reads the catalog, lets a person tick tools, and installs
only those bundles out of the zip into
%APPDATA%\\pyNavis\\extensions\\Playground.extension. Everything that decides
WHAT to copy or delete lives here so it is unit-tested outside Navisworks
(src/PyNavis.Tests/PlaygroundLibTests.cs); the bundle's script.py only fetches,
shows and calls in.

Paths in the catalog are forward-slash and relative to the extension folder.
A tool's id is its bundle key, which is also its folder.

Install rules:
  * a tool brings its own files, the bundle.yaml of every container on its
    path (tab, panel, stack, ...), and the catalog's shared files
  * hooks/*.py come along only while some installed tool needs a hook
  * a stack member brings its siblings: pyNavis skips a stack with fewer than
    two buttons, so half a stack would be a tool that never shows up
  * tools already on disk are left exactly as they are, edits and all
  * container layout lists are cut down to the folders actually present, so a
    Reload does not report every tool that was not installed as missing

Writes go into a temp folder beside the extension first and are moved into
place one bundle at a time, so a Reload never meets a half-written bundle.

No f-strings, for IronPython 3.4.
"""

import hashlib
import io
import json
import os
import re
import stat
import uuid
import zipfile

SCHEMA = 1
DEFAULT_CATALOG_URL = 'https://raw.githubusercontent.com/pynavis/playground/main/catalog.json'
EXTENSION_FOLDER = 'Playground.extension'
EXTENSION_YAML = 'name: Playground\nengine: ironpython\n'
STATUSES = ['untested', 'launched', 'verified', 'known-issue']
ALL = 'All'


class CatalogError(Exception):
    """The catalog cannot be used. The message is one plain sentence."""


# ---- paths ------------------------------------------------------------------

def data_dir():
    """%APPDATA%\\pyNavis\\playground: the cached catalog, zips and installed.json."""
    return os.path.join(os.environ.get('APPDATA', ''), 'pyNavis', 'playground')


def extension_dir(catalog=None):
    """Where installed tools land: the per-user extension root pyNavis always scans.

    Always Playground.extension, whatever the catalog says: Remove may delete
    this whole folder, so a catalog off the network never gets to choose it.
    The catalog argument is accepted for the callers that pass one."""
    return os.path.join(os.environ.get('APPDATA', ''), 'pyNavis', 'extensions', EXTENSION_FOLDER)


# Names Windows reads as devices whatever folder they sit in, with or without an extension.
_DEVICES = set(['con', 'prn', 'aux', 'nul'] + ['com%d' % i for i in range(1, 10)]
               + ['lpt%d' % i for i in range(1, 10)])
_BAD_CHARS = re.compile(r'[<>"|\x00-\x1f]')


def _safe_rel(path, glob=False):
    """True for a forward-slash path that stays inside the extension folder and
    means the same thing to Windows as it reads: no '..', no drive or share,
    no device name, no segment Windows would trim (a trailing dot or space).
    '*' and '?' are allowed only in a glob."""
    if not isinstance(path, str) or not path.strip():
        return False
    if '\\' in path or path.startswith('/') or ':' in path or _BAD_CHARS.search(path):
        return False
    if not glob and ('*' in path or '?' in path):
        return False
    for part in path.split('/'):
        if part in ('', '.', '..') or part != part.rstrip('. ') or part != part.lstrip(' '):
            return False
        if part.split('.', 1)[0].lower() in _DEVICES:
            return False
    return True


def _native(root, rel):
    return os.path.join(root, *rel.split('/'))


# ---- catalog ----------------------------------------------------------------

def parse_catalog(text):
    """The catalog as a dict, validated and with optional fields filled in.

    Raises CatalogError with a sentence fit for a toast."""
    try:
        data = json.loads(text)
    except Exception:
        raise CatalogError('The catalog could not be read: it is not valid JSON.')
    if not isinstance(data, dict):
        raise CatalogError('The catalog could not be read: it is not a JSON object.')
    if data.get('schema') != SCHEMA:
        raise CatalogError('The catalog uses schema %s, which needs a newer pyNavis.'
                           % data.get('schema'))
    for key in ('version', 'tools', 'download'):
        if key not in data:
            raise CatalogError('The catalog has no "%s".' % key)
    download = data['download']
    if not isinstance(download, dict) or not download.get('zip') or not download.get('sha256'):
        raise CatalogError('The catalog download needs both "zip" and "sha256".')
    if not isinstance(data['tools'], list):
        raise CatalogError('The catalog "tools" is not a list.')

    data.setdefault('name', 'pyNavis Playground')
    data.setdefault('extension', EXTENSION_FOLDER)
    # The folder is fixed (see extension_dir); a catalog naming any other is
    # refused rather than quietly installed somewhere it did not ask for.
    if not isinstance(data['extension'], str) or data['extension'].lower() != EXTENSION_FOLDER.lower():
        raise CatalogError('The catalog names the extension %r; only %s is supported.'
                           % (data['extension'], EXTENSION_FOLDER))
    data['extension'] = EXTENSION_FOLDER
    data.setdefault('shared', [])
    data.setdefault('containers', [])
    data['version'] = str(data['version'])
    for pattern in data['shared']:
        if not _safe_rel(pattern, glob=True):
            raise CatalogError('The catalog has a shared path outside the extension: %s' % pattern)

    for container in data['containers']:
        _check_entry(container, 'container')
        container.setdefault('files', [])
        _check_files(container)

    seen = set()
    for tool in data['tools']:
        _check_entry(tool, 'tool')
        if tool['id'] in seen:
            raise CatalogError('The catalog lists the tool %s twice.' % tool['id'])
        seen.add(tool['id'])
        parts = tool['id'].split('/')
        tool.setdefault('title', parts[-1].rsplit('.', 1)[0].replace('_', ' '))
        tool.setdefault('kind', parts[-1].rsplit('.', 1)[-1])
        tool.setdefault('tab', _title_of(parts, '.tab'))
        tool.setdefault('panel', _title_of(parts, '.panel'))
        tool.setdefault('container', '/'.join(parts[:-1]))
        tool.setdefault('summary', '')
        tool['summary'] = tool['summary'] or ''
        if tool.get('status') not in STATUSES:
            tool['status'] = 'untested'
        tool.setdefault('status_note', '')
        tool.setdefault('status_date', '')
        tool.setdefault('source', None)
        for key in ('hooks', 'siblings', 'files'):
            if not isinstance(tool.get(key), list):
                tool[key] = []
        _check_files(tool)
    return data


def _check_entry(entry, what):
    if not isinstance(entry, dict) or not _safe_rel(entry.get('id')):
        raise CatalogError('The catalog has a %s with a bad id path: %r'
                           % (what, (entry or {}).get('id') if isinstance(entry, dict) else entry))


def _check_files(entry):
    for path in entry.get('files') or []:
        if not _safe_rel(path):
            raise CatalogError('The catalog has a file path outside the extension: %s' % path)


def _title_of(parts, suffix):
    for part in parts:
        if part.endswith(suffix):
            return part[:-len(suffix)].replace('_', ' ')
    return ''


def tool_by_id(catalog):
    return dict((t['id'], t) for t in catalog['tools'])


def needs_newer_pynavis(catalog, version):
    """True when the catalog's min_pynavis is above this pyNavis version."""
    wanted = catalog.get('min_pynavis')
    if not wanted:
        return False
    return _version_key(str(wanted)) > _version_key(str(version))


def _version_key(text):
    out = []
    for part in text.split('.'):
        digits = re.match(r'\d+', part)
        out.append(int(digits.group(0)) if digits else 0)
    return out


def catalog_url(config):
    """('url', address) or ('file', path) from config.json's playground.catalog.

    No override, or a blank one, means the catalog on GitHub. Anything not
    starting http:// or https:// is a local path (dev and offline use);
    file:/// URLs are turned into a Windows path."""
    section = config.get('playground') if isinstance(config, dict) else None
    value = section.get('catalog') if isinstance(section, dict) else None
    if not isinstance(value, str) or not value.strip():
        return ('url', DEFAULT_CATALOG_URL)
    value = value.strip()
    lower = value.lower()
    if lower.startswith('http://') or lower.startswith('https://'):
        return ('url', value)
    if lower.startswith('file:///'):
        return ('file', value[8:].replace('/', '\\'))
    if lower.startswith('file://'):
        return ('file', '\\\\' + value[7:].replace('/', '\\'))
    return ('file', os.path.expandvars(value))


def zip_source(catalog, catalog_kind, catalog_location):
    """('url', address) or ('file', path) for the catalog's zip. A local
    catalog may name its zip by a path relative to the catalog file.

    A catalog that came off the network may only name a web address: a path
    there could point at a share on someone else's server, and merely opening
    it would hand over the user's Windows sign-in."""
    where = catalog['download']['zip'].strip()
    lower = where.lower()
    if lower.startswith('http://') or lower.startswith('https://'):
        return ('url', where)
    if catalog_kind != 'file':
        raise CatalogError('The catalog names its zip by a path, not a web address.')
    if lower.startswith('file:///'):
        return ('file', where[8:].replace('/', '\\'))
    where = where.replace('/', '\\')
    if not os.path.isabs(where) and catalog_kind == 'file':
        where = os.path.join(os.path.dirname(catalog_location), where)
    return ('file', where)


def zip_cache_path(catalog, folder=None):
    """<data dir>\\<version>.zip, the version made safe for a file name."""
    name = re.sub(r'[^0-9A-Za-z._-]', '_', catalog['version']) + '.zip'
    return os.path.join(folder or data_dir(), name)


def read_config(path):
    """config.json as a dict; {} when it is missing. An unreadable file raises
    ValueError, so nothing is ever written over a config a person can fix."""
    if not os.path.isfile(path):
        return {}
    with io.open(path, 'r', encoding='utf-8-sig') as f:
        text = f.read()
    if text.startswith('\ufeff'):
        text = text[1:]
    if not text.strip():
        return {}
    data = json.loads(text)
    if not isinstance(data, dict):
        raise ValueError('config.json is not a JSON object.')
    return data


def save_catalog_override(path, value):
    """Sets playground.catalog in config.json, or removes it when value is
    blank. Every other key is written back untouched."""
    data = read_config(path)
    value = (value or '').strip()
    section = data.get('playground')
    if not isinstance(section, dict):
        section = {}
    if value:
        section['catalog'] = value
    else:
        section.pop('catalog', None)
    if section:
        data['playground'] = section
    else:
        data.pop('playground', None)
    _write_text(path, json.dumps(data, indent=2) + '\n')


def web_url(value):
    """The value when it is an http(s) address, else None. A catalog comes off
    the network, and handing anything else to the shell could start a program."""
    if not isinstance(value, str):
        return None
    value = value.strip()
    lower = value.lower()
    if (lower.startswith('https://') or lower.startswith('http://')) and ' ' not in value:
        return value
    return None


def sibling_note(catalog, tool):
    """'Installs with X and Y' for a stack member, '' for anything else."""
    by_id = tool_by_id(catalog)
    titles = [by_id[s]['title'] for s in tool.get('siblings') or [] if s in by_id]
    if not titles:
        return ''
    return 'Installs with ' + join_and(titles)


def join_and(words):
    """'a', 'a and b', 'a, b and c'."""
    if len(words) == 1:
        return words[0]
    return ', '.join(words[:-1]) + ' and ' + words[-1]


def filter_tools(tools, text, status):
    """Tools whose title, summary, tab or panel contain every word of text,
    with the given status ('All' for any). Catalog order is kept."""
    words = (text or '').lower().split()
    out = []
    for tool in tools:
        if status and status != ALL and tool.get('status') != status:
            continue
        hay = ' '.join([tool.get('title') or '', tool.get('summary') or '',
                        tool.get('tab') or '', tool.get('panel') or '']).lower()
        if all(w in hay for w in words):
            out.append(tool)
    return out


# ---- plans ------------------------------------------------------------------

def _ordered(catalog, ids):
    wanted = set(ids)
    return [t['id'] for t in catalog['tools'] if t['id'] in wanted]


def _containers_of(catalog, tool_ids):
    out = []
    for container in catalog['containers']:
        prefix = container['id'] + '/'
        if any(t.startswith(prefix) for t in tool_ids):
            out.append(container)
    return out


def install_plan(catalog, selected_ids, installed_ids):
    """What to install for the ticked tools.

    {'tools': ids to install (catalog order), 'added': {id: reason} for stack
    siblings pulled in, 'files': exact paths, 'shared': shared globs to take
    from the zip, 'hooks': whether hooks/*.py are among them}.
    Tools already installed are left alone, and nothing at all is planned when
    every ticked tool is already there."""
    by_id = tool_by_id(catalog)
    installed = set(i for i in installed_ids if i in by_id)
    new = [i for i in _ordered(catalog, selected_ids) if i not in installed]
    added = {}
    for tid in list(new):
        for sib in by_id[tid]['siblings']:
            if sib in by_id and sib not in installed and sib not in new and sib not in added:
                added[sib] = 'Comes with %s, they share a stack' % by_id[tid]['title']
    tools = _ordered(catalog, new + list(added))
    if not tools:
        return {'tools': [], 'added': {}, 'files': [], 'shared': [], 'hooks': False}

    files = []
    for tid in tools:
        files.extend(by_id[tid]['files'])
    for container in _containers_of(catalog, tools):
        files.extend(container['files'])
    hooks = any(by_id[t]['hooks'] for t in set(tools) | installed)
    shared = [p for p in catalog['shared'] if hooks or not _is_hook_glob(p)]
    return {'tools': tools, 'added': added, 'files': sorted(set(files)),
            'shared': shared, 'hooks': hooks}


def _is_hook_glob(pattern):
    return pattern == 'hooks' or pattern.startswith('hooks/')


def remove_plan(catalog, remove_ids, installed_ids):
    """What to delete for the unticked installed tools.

    {'tools': ids to delete, 'added': {id: reason} for stack siblings that go
    too, 'files': their files, 'containers': container ids left with no tool,
    deepest first, 'hooks': whether hooks/ goes because no remaining tool needs
    one, 'everything': whether no tool remains, so the extension goes}."""
    by_id = tool_by_id(catalog)
    installed = [i for i in _ordered(catalog, installed_ids)]
    gone = [i for i in _ordered(catalog, remove_ids) if i in installed]
    added = {}
    for tid in list(gone):
        for sib in by_id[tid]['siblings']:
            if sib in installed and sib not in gone and sib not in added:
                added[sib] = 'Goes with %s, they share a stack' % by_id[tid]['title']
    tools = _ordered(catalog, gone + list(added))
    if not tools:
        return {'tools': [], 'added': {}, 'files': [], 'containers': [],
                'hooks': False, 'everything': False}
    remaining = [i for i in installed if i not in tools]
    files = []
    for tid in tools:
        files.extend(by_id[tid]['files'])
    keep = set(c['id'] for c in _containers_of(catalog, remaining))
    empty = [c['id'] for c in _containers_of(catalog, tools) if c['id'] not in keep]
    empty.sort(key=lambda cid: (-cid.count('/'), cid))
    had_hooks = any(by_id[t]['hooks'] for t in installed)
    still_hooks = any(by_id[t]['hooks'] for t in remaining)
    return {'tools': tools, 'added': added, 'files': sorted(set(files)),
            'containers': empty, 'hooks': had_hooks and not still_hooks,
            'everything': not remaining}


# ---- zip entries ------------------------------------------------------------

def _glob_regex(pattern):
    out = []
    i = 0
    while i < len(pattern):
        if pattern.startswith('**', i):
            out.append('.*')
            i += 2
        elif pattern[i] == '*':
            out.append('[^/]*')
            i += 1
        elif pattern[i] == '?':
            out.append('[^/]')
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile('^' + ''.join(out) + '$')


def zip_prefix(names, extension):
    """'<extension>/' when every file in the zip sits under that folder, else ''."""
    files = [n for n in names if not n.endswith('/')]
    head = extension + '/'
    if files and all(n.startswith(head) for n in files):
        return head
    return ''


def zip_entries(plan, names, extension):
    """{zip entry name: extension-relative path} for exactly what the plan takes."""
    prefix = zip_prefix(names, extension)
    wanted = set(plan['files'])
    globs = [_glob_regex(p) for p in plan['shared']]
    out = {}
    for name in names:
        if name.endswith('/') or not name.startswith(prefix):
            continue
        rel = name[len(prefix):]
        if not _safe_rel(rel):
            continue
        if rel in wanted or any(g.match(rel) for g in globs):
            out[name] = rel
    return out


# ---- on disk ----------------------------------------------------------------

def installed_on_disk(catalog, ext_root):
    """Catalog tool ids whose bundle folder exists, in catalog order. The truth
    on disk wins over installed.json, which a manual delete would not update."""
    return [t['id'] for t in catalog['tools']
            if os.path.isdir(_native(ext_root, t['id']))]


def _units(rels, tool_ids, shared):
    """Groups extension-relative files into what moves into place as one piece:
    a whole bundle folder, a whole '**' library folder, or a single file."""
    folders = sorted(tool_ids, key=len, reverse=True)
    trees = [p[:-3] for p in shared if p.endswith('/**')]
    units = {}
    for rel in rels:
        unit = None
        for tid in folders:
            if rel.startswith(tid + '/'):
                unit = tid
                break
        if unit is None:
            for tree in trees:
                if rel.startswith(tree + '/'):
                    unit = tree
                    break
        units.setdefault(unit or rel, []).append(rel)
    return units


def install_files(zip_path, plan, ext_root):
    """Extracts the plan from the zip into a temp folder beside ext_root, then
    moves each bundle into place. Returns the number of tools installed."""
    if not plan['tools']:
        return 0
    parent = os.path.dirname(os.path.abspath(ext_root))
    if not os.path.isdir(parent):
        os.makedirs(parent)
    stage = os.path.join(parent, '.playground-' + uuid.uuid4().hex)
    try:
        archive = zipfile.ZipFile(zip_path)
        try:
            entries = zip_entries(plan, archive.namelist(), os.path.basename(ext_root))
            for name, rel in entries.items():
                target = _native(stage, rel)
                folder = os.path.dirname(target)
                if not os.path.isdir(folder):
                    os.makedirs(folder)
                with open(target, 'wb') as out:
                    out.write(archive.read(name))
        finally:
            archive.close()
        missing = [t for t in plan['tools'] if not os.path.isdir(_native(stage, t))]
        if missing:
            raise IOError('The download does not hold %s.' % missing[0])

        if not os.path.isdir(ext_root):
            os.makedirs(ext_root)
        units = _units(list(entries.values()), plan['tools'], plan['shared'])
        # Shared files and containers first, bundles last: a bundle is only
        # ever seen with its library and its panel already there.
        order = sorted(units, key=lambda u: (u in plan['tools'], u))
        for unit in order:
            _move_into_place(_native(stage, unit), _native(ext_root, unit), stage)
        if not os.path.exists(os.path.join(ext_root, 'extension.yaml')):
            _write_text(os.path.join(ext_root, 'extension.yaml'), EXTENSION_YAML)
        prune_layouts(ext_root)
    finally:
        if os.path.isdir(stage):
            try:
                _rmtree(stage)
            except Exception:
                pass
    return len(plan['tools'])


def _move_into_place(source, target, stage):
    folder = os.path.dirname(target)
    if not os.path.isdir(folder):
        os.makedirs(folder)
    if os.path.isdir(source):
        if os.path.isdir(target):
            old = os.path.join(stage, '.old-' + uuid.uuid4().hex)
            os.rename(target, old)
        os.rename(source, target)
    else:
        if os.path.isdir(target):
            _rmtree(target)
        os.replace(source, target)


def remove_files(ext_root, plan):
    """Deletes what the remove plan names, then trims the layouts."""
    if not plan['tools'] or not os.path.isdir(ext_root):
        return 0
    if plan['everything']:
        _rmtree(ext_root)
        return len(plan['tools'])
    for tid in plan['tools']:
        path = _native(ext_root, tid)
        if os.path.isdir(path):
            _rmtree(path)
    for cid in plan['containers']:
        path = _native(ext_root, cid)
        # A container someone added their own bundle to is kept.
        if os.path.isdir(path) and not [n for n in os.listdir(path)
                                        if os.path.isdir(os.path.join(path, n))]:
            _rmtree(path)
    if plan['hooks']:
        hooks = os.path.join(ext_root, 'hooks')
        if os.path.isdir(hooks):
            _rmtree(hooks)
    prune_layouts(ext_root)
    return len(plan['tools'])


def _is_link(path):
    """True for a symbolic link or a junction. IronPython's os.path.islink does
    not see junctions, so the file attributes are asked directly."""
    try:
        from System.IO import File, FileAttributes
    except ImportError:
        return os.path.islink(path)
    try:
        attributes = File.GetAttributes(path)
    except Exception:
        return os.path.islink(path)
    return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint


def _rmtree(path):
    """Deletes a folder and what is in it, but never follows a link: a linked
    folder (someone's working copy, say) loses the link and keeps its files.
    Read-only files are made writable first."""
    if _is_link(path):
        if os.path.isdir(path):
            os.rmdir(path)
        else:
            os.remove(path)
        return
    for name in os.listdir(path):
        child = os.path.join(path, name)
        if _is_link(child):
            if os.path.isdir(child):
                os.rmdir(child)
            else:
                os.remove(child)
        elif os.path.isdir(child):
            _rmtree(child)
        else:
            try:
                os.remove(child)
            except OSError:
                os.chmod(child, stat.S_IWRITE)
                os.remove(child)
    os.rmdir(path)


# ---- layouts ----------------------------------------------------------------

# Read the way pyNavis's BundleYaml reads them: the key in any case, items as
# "- name" at any indent, blank and # comment lines inside the list skipped.
_LAYOUT_KEY = re.compile(r'^\s*layout\s*:\s*$', re.IGNORECASE)
_LIST_ITEM = re.compile(r'^\s*- (.*?)\s*$')
_SKIPPED = re.compile(r'^\s*(#.*)?$')


def _bare(name):
    name = name.strip().strip('"\'')
    dot = name.rfind('.')
    return (name[:dot] if dot > 0 else name).lower()


def filter_layout(text, present):
    """The yaml text with its layout: list cut to the names in present.

    Entries and names compare without their .suffix and ignoring case, the
    way pyNavis matches them. An empty list loses its layout: key too."""
    keep = set(_bare(n) for n in present)
    lines = text.splitlines(True)
    out = []
    i = 0
    while i < len(lines):
        line = lines[i]
        if _LAYOUT_KEY.match(line.rstrip('\r\n')):
            # The block runs to its last item; blank or comment lines after that
            # belong to whatever follows and are left where they are.
            end = i + 1
            j = i + 1
            while j < len(lines):
                bare = lines[j].rstrip('\r\n')
                if _LIST_ITEM.match(bare):
                    end = j + 1
                elif not _SKIPPED.match(bare):
                    break
                j += 1
            block = []
            kept = False
            for item in lines[i + 1:end]:
                match = _LIST_ITEM.match(item.rstrip('\r\n'))
                if match is None:
                    block.append(item)
                elif _bare(match.group(1)) in keep:
                    block.append(item)
                    kept = True
            if kept:
                out.append(line)
                out.extend(block)
            i = end
            continue
        out.append(line)
        i += 1
    return ''.join(out)


_CONTAINER_SUFFIXES = ('.tab', '.panel', '.stack', '.pulldown', '.slideout',
                       '.splitbutton', '.splitpushbutton')


def prune_layouts(ext_root):
    """Cuts extension.yaml and every container's layout to the folders on disk."""
    yamls = [os.path.join(ext_root, 'extension.yaml')]
    for folder, dirs, _files in os.walk(ext_root):
        for name in dirs:
            if name.lower().endswith(_CONTAINER_SUFFIXES):
                yamls.append(os.path.join(folder, name, 'bundle.yaml'))
    for path in yamls:
        if not os.path.isfile(path):
            continue
        folder = os.path.dirname(path)
        present = [n for n in os.listdir(folder) if os.path.isdir(os.path.join(folder, n))]
        with io.open(path, 'r', encoding='utf-8', newline='') as f:
            text = f.read()
        trimmed = filter_layout(text, present)
        if trimmed != text:
            _write_text(path, trimmed)


# ---- installed.json -----------------------------------------------------------

def _empty_installed():
    return {'catalog_version': None, 'tools': {}}


def read_installed(path):
    """{'catalog_version': str or None, 'tools': {id: {'date', 'version'}}}.
    A missing or unreadable file reads as nothing installed."""
    try:
        with io.open(path, 'r', encoding='utf-8-sig') as f:
            data = json.loads(f.read())
    except Exception:
        return _empty_installed()
    if not isinstance(data, dict) or not isinstance(data.get('tools'), dict):
        return _empty_installed()
    return {'catalog_version': data.get('catalog_version'), 'tools': data['tools']}


def write_installed(path, data):
    _write_text(path, json.dumps(data, indent=2, sort_keys=True) + '\n')


def record_install(data, catalog, tool_ids, date):
    tools = dict(data.get('tools') or {})
    for tid in tool_ids:
        tools[tid] = {'date': date, 'version': catalog['version']}
    return {'catalog_version': catalog['version'], 'tools': tools}


def record_remove(data, tool_ids):
    tools = dict(data.get('tools') or {})
    for tid in tool_ids:
        tools.pop(tid, None)
    return {'catalog_version': data.get('catalog_version'), 'tools': tools}


def cache_path(folder=None):
    """Where the last catalog that parsed is kept, for when GitHub is out of reach."""
    return os.path.join(folder or data_dir(), 'catalog.json')


def load_cached(folder=None):
    """The cached catalog, parsed, or None when there is none or it is unusable."""
    try:
        with io.open(cache_path(folder), 'r', encoding='utf-8-sig') as f:
            return parse_catalog(f.read())
    except Exception:
        return None


def save_cached(text, folder=None):
    _write_text(cache_path(folder), text)


def _write_text(path, text):
    """Atomic: written beside the target, then swapped in."""
    folder = os.path.dirname(path)
    if folder and not os.path.isdir(folder):
        os.makedirs(folder)
    temp = path + '.tmp'
    with io.open(temp, 'w', encoding='utf-8', newline='') as f:
        f.write(text)
    os.replace(temp, path)


# ---- sha256 -----------------------------------------------------------------

def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as f:
        while True:
            block = f.read(1 << 20)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def verify_sha256(path, expected):
    """True when the file's SHA-256 is the expected hex digest (any case)."""
    if not expected:
        return False
    return sha256_file(path) == expected.strip().lower()
