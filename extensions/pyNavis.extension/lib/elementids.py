"""Element IDs as plain text, with no Navisworks in sight.

Two conventions carry a Revit element id into a federated model, and this
module understands both:

  * an NWC written by the Revit exporter puts the id in a property tab,
    "Element ID" > "Value", which the bundles read through pynavis.props
  * a model published straight into design coordination has no such tab; the
    id is baked into the item's name instead, as "Basic Wall [123456]"

An "Element ID" tab is not proof of an element. The nested parts under a
family instance carry the tab too, holding the TYPE's id (Revit Type > Id on
the instance) - the same number on every instance of that type in the model.
On a 1.75M-item federation the type id 12345678 sat on 16 nested parts under
16 different fixtures, none of them an element, while the fixture itself was
12345679. Both bundles therefore only treat a node as an element when it
carries ELEMENT_CATEGORY: IDs of Selection reads the id off the nearest such
ancestor, and Select by IDs requires it in the search, so a type id finds
nothing rather than "the first instance of that family".

An element id then normally resolves to one item, and Select by IDs stops at
the first hit (~0.80s per id). every_match keeps walking for every copy - the
case is the same element exported into two sibling files - at a full walk of
the document per id, ~3.0s, because a search that must not stop early cannot
stop early. There is no cheaper route to the whole set: an OR search costs one
full walk per term, scoping to any ancestor short of the document root misses
copies in sibling files, and PruneBelowMatch changes nothing (all measured).

Everything here takes strings and returns strings, so it is unit-tested
outside Navisworks (src/PyNavis.Tests/ElementIdsTests.cs). No f-strings, for
IronPython 3.4 compatibility.
"""

import re

# Clipboard text longer than this is ignored rather than scanned. Select by
# IDs reads the clipboard on every run to fill its box in, and whatever the
# user last copied is usually not a list of ids; a copied spreadsheet or a
# document does not deserve a regex pass over megabytes.
MAX_CLIPBOARD_CHARS = 100000

# One settings store shared by both bundles rather than one each: which
# property holds the id is a fact about the model, not about the button, and
# the settings dialog reached by Shift+Click on either button edits this.
SETTINGS_KEY = 'cfg_element_ids'

DEFAULTS = {
    'id_category': 'Element ID',    # the Revit exporter's property tab
    'id_property': 'Value',
    'zoom': True,                   # zoom onto what was selected
    'isolate': False,               # hide everything else as well
    'use_clipboard': True,          # prefill the paste box from the clipboard
    'every_match': False,           # select every copy, not one node per id
}

# The property category the Revit exporter puts on every real element and on
# nothing else, by INTERNAL name so it survives a non-English Navisworks. This
# is what tells an element apart from the nested parts inside it: a family
# instance carries "Element ID" = its own id, but so does every nested part
# under it - with the TYPE's id instead (Revit Type > Id on the instance), the
# same number on every instance of that type in the model. Searching for a
# type id without this check finds "the first instance of that family", which
# is what a user who typed an element id sees as the wrong element; reading
# an id off a selected part without it copies the type id out in the first
# place. Field-checked on LF-S22D lighting fixtures: instance
# 12345679 carried the category, its 16-a-side nested parts (all 12345678)
# did not.
ELEMENT_CATEGORY = 'LcRevitData_Element'

_SEPARATORS = re.compile(r'[,;\s]+')

# Square brackets only. Round brackets are common in ordinary item names
# ("Pipe Type (2)"), so treating them as an id marker would invent ids out of
# copy suffixes. parse_ids is looser because there a human typed the token on
# purpose.
_SQUARE = re.compile(r'\[\s*(\d+)\s*\]')

_TOKEN_EDGES = '[](){}<>"\'`.:'


def parse_ids(text):
    """(ids, rejected) for text a human typed or pasted into the dialog.

    Splits on commas, semicolons and any whitespace, strips bracket and
    punctuation noise off each token, and keeps the all-digit ones as ids in
    first-seen order with duplicates removed. Every other token comes back in
    rejected, verbatim, so the caller can say which ones it ignored.
    """
    if not text:
        return [], []

    ids = []
    seen = {}
    rejected = []
    for token in _SEPARATORS.split(str(text)):
        if not token:
            continue
        stripped = token.strip(_TOKEN_EDGES)
        if stripped.isdigit():
            normalized = _normalize(stripped)
            if normalized not in seen:
                seen[normalized] = True
                ids.append(normalized)
        else:
            rejected.append(token)
    return ids, rejected


def bracket_ids(text):
    """Every "[123456]" in text as ids, in order, duplicates removed.

    This is the design coordination case read in bulk: a block of item names
    copied out of Navisworks yields one id per name.
    """
    if not text:
        return []

    ids = []
    seen = {}
    for digits in _SQUARE.findall(str(text)):
        normalized = _normalize(digits)
        if normalized not in seen:
            seen[normalized] = True
            ids.append(normalized)
    return ids


def clipboard_ids(text):
    """The ids in clipboard text, or [] when it does not look like ids at all.

    Select by IDs runs this to decide what its box opens with, so it has to be
    strict enough that ordinary copied text never lands in there looking like
    a list someone meant to search. Two shapes are accepted, and nothing else:

      * every token parses as a number ("123456, 234567"), which is what a
        clash report or a spreadsheet column looks like
      * the text carries bracketed ids ("Basic Wall [123456]"), which is what
        copying item names out of a design coordination model looks like

    Prose that merely happens to contain a number fails both and comes back
    empty, so the box opens blank rather than prefilled with nonsense.
    """
    if not text or len(str(text)) > MAX_CLIPBOARD_CHARS:
        return []

    ids, rejected = parse_ids(text)
    if ids and not rejected:
        return ids
    return bracket_ids(text)


def id_from_name(name):
    """The id in an item's display name ("Basic Wall [123456]"), or None.

    The last all-digit bracket wins, so a name that also carries a bracketed
    type ("Wall [Type A] [123456]") still resolves. Returned exactly as it
    appears in the model, unlike the input-side helpers above: this feeds a
    report of what the model actually says.
    """
    if not name:
        return None
    found = _SQUARE.findall(str(name))
    return found[-1] if found else None


def is_element(categories):
    """True when a pynavis.props.categories() listing - (display, name) pairs -
    includes the exporter's element category. See ELEMENT_CATEGORY."""
    return any(name == ELEMENT_CATEGORY for _display, name in categories)


def format_id_list(ids):
    """The ids as one comma-separated line, ready for the clipboard."""
    return ', '.join(str(i) for i in ids)


def _normalize(digits):
    """Leading zeros dropped, so a pasted "000123456" matches the stored id."""
    return str(int(digits))
