"""REPL-style execution for the pyNavis console (pure python, unit-tested).

The console must behave like an interactive prompt: if the input ends in an
expression, its value is echoed - including after leading statements, e.g.
"import math; math.floor(2.5)". A plain exec() discards that trailing value,
so the input is split at the last top-level statement and the tail eval'd.
"""

import ast
import sys
import traceback
from io import StringIO


def execute(code, namespace):
    """Runs code in namespace; returns everything the run printed, the repr of
    a trailing expression (when not None), or a formatted traceback.
    """
    buffer = StringIO()
    old_out, old_err = sys.stdout, sys.stderr
    sys.stdout = sys.stderr = buffer
    try:
        head, tail = _split_trailing_expression(code)
        if head.strip():
            exec(compile(head, '<console>', 'exec'), namespace)
        if tail is not None:
            result = eval(compile(tail, '<console>', 'eval'), namespace)
            if result is not None:
                print(repr(result))
    except Exception:
        traceback.print_exc()
    finally:
        sys.stdout, sys.stderr = old_out, old_err
    return buffer.getvalue()


def _split_trailing_expression(code):
    """(statements, trailing_expression) - expression is None when the input
    does not end in one. IronPython's compile() cannot take AST objects, so the
    AST is only used for the split position and the source is sliced.
    """
    tree = ast.parse(code, '<console>', 'exec')
    if not tree.body or not isinstance(tree.body[-1], ast.Expr):
        return code, None

    last = tree.body[-1]
    lines = code.split('\n')
    row, col = last.lineno - 1, last.col_offset
    head = '\n'.join(lines[:row] + [lines[row][:col]])
    tail = '\n'.join([lines[row][col:]] + lines[row + 1:])
    return head, tail
