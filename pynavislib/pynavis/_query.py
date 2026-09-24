"""Pure query spec model for search sets (no host/Navisworks dependencies).

The query model is a pure data structure representing a search predicate, with
serialization to/from dict (the Excel/data-driven contract) and validation.
No f-strings, for IronPython 3.4 compatibility.
"""


OPS = {
    'equals': True,
    'not_equals': True,
    'contains': True,
    'wildcard': True,
    'gt': True,
    'ge': True,
    'lt': True,
    'le': True,
    'has_property': False,
    'not_has_property': False,
    'has_category': False,
}


class Condition(object):
    """One atomic condition in a search query.

    Fields:
        category (str): item category name (e.g. 'Item', 'Element')
        prop (str): property name within the category (e.g. 'Type', 'Level')
        op (str): operation (one of OPS keys)
        value: the value to test against; can be None if op doesn't need it
        by_display (bool): if True, match against display name; default True
        ignore_case (bool): if True, case-insensitive match; default False
    """

    def __init__(self, category, prop, op, value=None, by_display=True, ignore_case=False):
        self.category = category
        self.prop = prop
        self.op = op
        self.value = value
        self.by_display = by_display
        self.ignore_case = ignore_case

    def to_dict(self):
        """Serialize to dict, omitting default-false flags."""
        d = {
            'category': self.category,
            'prop': self.prop,
            'op': self.op,
        }
        if self.value is not None:
            d['value'] = self.value
        if self.ignore_case:
            d['ignore_case'] = True
        if not self.by_display:
            d['by_display'] = False
        return d

    @staticmethod
    def from_dict(data):
        """Deserialize from dict, tolerant of missing optional keys."""
        return Condition(
            category=data.get('category', ''),
            prop=data.get('prop', ''),
            op=data.get('op', ''),
            value=data.get('value'),
            by_display=data.get('by_display', True),
            ignore_case=data.get('ignore_case', False),
        )


class Query(object):
    """A search query: a list of AND groups ORed together.

    Fields:
        groups (list of lists of Condition): outer list ORed, inner list ANDed
        locations (str): 'default' (v1 only), or location filter scope
        prune (bool): whether to prune non-matching items; default False
    """

    def __init__(self, groups=None, locations='default', prune=False):
        self.groups = groups or []
        self.locations = locations
        self.prune = prune

    def to_dict(self):
        """Serialize to dict: {'or': [{'and': [cond, ...]}, ...]}"""
        return {
            'or': [{'and': [c.to_dict() for c in group]} for group in self.groups]
        }

    @staticmethod
    def from_dict(data):
        """Deserialize from dict, tolerant of missing optional keys."""
        groups = []
        for or_item in data.get('or', []):
            group = [Condition.from_dict(c) for c in or_item.get('and', [])]
            groups.append(group)
        return Query(groups=groups)


def validate(query):
    """Validate a Query; return list of problem strings (empty if valid).

    Checks:
        - Every AND group holds at least one condition (an EMPTY group is a
          match-everything predicate, never something a caller meant: it
          compiles to a Search with no conditions, which selects the whole
          model. See sets._validate_import, which leans on this check to
          reject a blank Excel row rather than writing a match-everything
          search set into the user's document.)
        - Every Condition has a nonempty category and prop (except has_category
          which needs no prop)
        - Every Condition has a known op
        - If op requires a value, the value must be present (not None)
    """
    problems = []
    for group_idx, group in enumerate(query.groups):
        if not group:
            problems.append('Group {0} has no conditions'.format(group_idx))
        for cond_idx, cond in enumerate(group):
            prefix = 'Group {0} Condition {1}: '.format(group_idx, cond_idx)

            # Check category
            if not cond.category:
                problems.append(prefix + 'category must not be empty')

            # Check prop (except for has_category)
            if cond.op != 'has_category' and not cond.prop:
                problems.append(prefix + 'prop must not be empty (except for has_category)')

            # Check op is known
            if cond.op not in OPS:
                problems.append(prefix + 'unknown op: ' + cond.op)

            # Check value presence if required by op
            if OPS.get(cond.op, False) and cond.value is None:
                problems.append(prefix + 'op ' + cond.op + ' requires a value')

    return problems


class Builder(object):
    """Fluent builder for Query objects.

    Methods chain by returning self. A _pending condition is started by prop()
    or category() and completed by an operator (equals, contains, etc.) into
    the current group. or_group() flushes the current AND group. build()
    flushes, validates, and raises ValueError listing problems.
    """

    def __init__(self):
        self.groups = []  # List of condition groups (each group is a list)
        self.current_group = []  # Current AND group being built
        self._pending = None  # Condition being built (started by prop/category)

    def prop(self, category, name):
        """Start a condition with a category and property name."""
        if self._pending is not None:
            raise ValueError('Cannot start new prop: previous condition incomplete (missing operator)')
        self._pending = Condition(category=category, prop=name, op=None)
        return self

    def and_prop(self, category, name):
        """Alias for prop() for natural readability."""
        return self.prop(category, name)

    def category(self, name):
        """Start a has_category condition (no prop needed)."""
        if self._pending is not None:
            raise ValueError('Cannot start new condition: previous condition incomplete (missing operator)')
        # has_category is immediately complete
        self.current_group.append(Condition(category=name, prop='', op='has_category'))
        return self

    def _complete_condition(self, op, value=None):
        """Complete the pending condition with an operator and optional value."""
        if self._pending is None:
            raise ValueError('Cannot apply operator: no property started')
        self._pending.op = op
        self._pending.value = value
        self.current_group.append(self._pending)
        self._pending = None
        return self

    def equals(self, value):
        """Complete condition with equals operator."""
        return self._complete_condition('equals', value)

    def not_equals(self, value):
        """Complete condition with not_equals operator."""
        return self._complete_condition('not_equals', value)

    def contains(self, text):
        """Complete condition with contains operator."""
        return self._complete_condition('contains', text)

    def wildcard(self, pattern):
        """Complete condition with wildcard operator."""
        return self._complete_condition('wildcard', pattern)

    def gt(self, number):
        """Complete condition with gt (greater than) operator."""
        return self._complete_condition('gt', number)

    def ge(self, number):
        """Complete condition with ge (greater or equal) operator."""
        return self._complete_condition('ge', number)

    def lt(self, number):
        """Complete condition with lt (less than) operator."""
        return self._complete_condition('lt', number)

    def le(self, number):
        """Complete condition with le (less or equal) operator."""
        return self._complete_condition('le', number)

    def exists(self):
        """Complete condition with has_property operator (no value needed)."""
        return self._complete_condition('has_property')

    def missing(self):
        """Complete condition with not_has_property operator (no value needed)."""
        return self._complete_condition('not_has_property')

    def ignore_case(self):
        """Set ignore_case=True on the last condition in the current group.

        Raises ValueError when a condition is still pending (a prop() with no
        operator yet): the flag would otherwise land silently on the PREVIOUS
        condition, which is never what the call site meant. Complete the
        condition with an operator first, then flag it.
        """
        if self._pending is not None:
            raise ValueError(
                'Cannot set ignore_case: the current condition is incomplete '
                '(missing operator) - apply the operator first, so the flag '
                'lands on it rather than on the previous condition')
        if not self.current_group:
            raise ValueError('Cannot set ignore_case: no condition in current group')
        self.current_group[-1].ignore_case = True
        return self

    def or_group(self):
        """Close the current AND group and start a new one."""
        if self._pending is not None:
            raise ValueError('Cannot close group: previous condition incomplete (missing operator)')
        if self.current_group:
            self.groups.append(self.current_group)
            self.current_group = []
        return self

    def build(self):
        """Flush current group, validate, and return a Query object.

        Raises ValueError if any validation problems are found.
        """
        # Check for dangling condition
        if self._pending is not None:
            raise ValueError('Incomplete condition: property started but no operator applied')

        # Flush current group if it has conditions
        if self.current_group:
            self.groups.append(self.current_group)
            self.current_group = []

        # Create Query and validate
        query = Query(groups=self.groups)
        problems = validate(query)
        if problems:
            raise ValueError('Query validation failed: ' + '; '.join(problems))

        return query
