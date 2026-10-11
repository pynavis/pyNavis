"""No stray control bytes in the tools (python -m unittest discover -s tests/tools).

A "\\b" that passes through one escaping layer too many lands in the file as a
backspace byte (0x08), and Inno Setup and PowerShell read it as part of the text:
pyNavis.iss once handed protect-extension "{app}<backspace>ackups" as its backup
folder. Tab, CR and LF are the only control bytes a text file here needs.
"""
import os
import shutil
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TOOLS = os.path.join(ROOT, 'tools')

TEXT_EXTENSIONS = ('.iss', '.ps1', '.py', '.txt')
ALLOWED = (0x09, 0x0A, 0x0D)


def control_bytes(folder):
    """(path relative to folder, line number, byte) for every control byte other than
    tab, CR and LF in the text files under folder."""
    found = []
    for dirpath, dirnames, filenames in os.walk(folder):
        dirnames[:] = sorted(d for d in dirnames if d != '__pycache__')
        for name in sorted(filenames):
            if not name.lower().endswith(TEXT_EXTENSIONS):
                continue
            path = os.path.join(dirpath, name)
            with open(path, 'rb') as fh:
                data = fh.read()
            rel = os.path.relpath(path, folder).replace(os.sep, '/')
            for number, line in enumerate(data.split(b'\n'), 1):
                for byte in bytearray(line):
                    if byte < 0x20 and byte not in ALLOWED:
                        found.append((rel, number, byte))
    return found


def describe(found, prefix):
    return '\n'.join('%s%s line %d: control byte 0x%02X' % (prefix, rel, number, byte)
                     for rel, number, byte in found)


class ControlBytesTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.tmp)

    def write(self, rel, data):
        path = os.path.join(self.tmp, rel.replace('/', os.sep))
        if not os.path.isdir(os.path.dirname(path)):
            os.makedirs(os.path.dirname(path))
        with open(path, 'wb') as fh:
            fh.write(data)

    def test_names_the_file_and_line_of_a_backspace(self):
        self.write('installer/setup.iss', b"; one\r\nExpandConstant('{app}\x08ackups')\r\n")
        self.assertEqual(control_bytes(self.tmp), [('installer/setup.iss', 2, 0x08)])

    def test_tab_cr_and_lf_are_fine(self):
        self.write('install.ps1', b"if ($a) {\r\n\tWrite-Host 'ok'\r\n}\n")
        self.assertEqual(control_bytes(self.tmp), [])

    def test_every_other_control_byte_is_reported(self):
        bad = [b for b in range(0x20) if b not in ALLOWED]
        self.write('tool.py', b''.join(b'x' + bytearray([b]) + b'\n' for b in bad))
        self.assertEqual([byte for _rel, _n, byte in control_bytes(self.tmp)], bad)
        self.assertEqual([n for _rel, n, _byte in control_bytes(self.tmp)], list(range(1, len(bad) + 1)))

    def test_only_text_files_are_read(self):
        self.write('icon.png', b'\x89PNG\r\n\x1a\n\x00\x00')
        self.assertEqual(control_bytes(self.tmp), [])

    def test_tools_hold_no_control_bytes(self):
        found = control_bytes(TOOLS)
        if found:
            self.fail('Stray control bytes (a mangled escape such as \\b?):\n' + describe(found, 'tools/'))


if __name__ == '__main__':
    unittest.main()
