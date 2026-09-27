"""Lists what is in memory, with a clickable link per item."""

from pynavis import memory, output

state, rows = memory.contents()
if not state['items']:
    output.print_md('**Memory is empty.** Select something and press Remember.')
else:
    output.print_html(memory.contents_html(rows, state))
