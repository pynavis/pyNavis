"""Tests for the Source section of the tools guide (python -m unittest discover -s tests/tools)."""
import io
import os
import shutil
import sys
import tempfile
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, 'tools'))

import build_tools_site as site  # noqa: E402


def write(folder, rel, text):
    path = os.path.join(folder, rel.replace('/', os.sep))
    if not os.path.isdir(os.path.dirname(path)):
        os.makedirs(os.path.dirname(path))
    with io.open(path, 'w', encoding='utf-8') as fh:
        fh.write(text)


class SourceFilesTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.tmp)

    def test_lists_the_four_bundle_files_in_fixed_order(self):
        for name in ('bundle.yaml', 'pane.xaml', 'icon.png', 'config.py', 'script.py', 'notes.txt'):
            write(self.tmp, name, name)
        self.assertEqual([rel for rel, _lang in site.source_files(self.tmp)],
                         ['script.py', 'config.py', 'pane.xaml', 'bundle.yaml'])

    def test_language_follows_the_extension(self):
        for name in ('script.py', 'pane.xaml', 'bundle.yaml'):
            write(self.tmp, name, name)
        self.assertEqual(dict(site.source_files(self.tmp)),
                         {'script.py': 'python', 'pane.xaml': 'xml', 'bundle.yaml': 'yaml'})

    def test_container_without_a_script_lists_its_children(self):
        write(self.tmp, 'bundle.yaml', 'title: More')
        write(self.tmp, 'Purge.pushbutton/script.py', 'pass')
        write(self.tmp, 'Purge.pushbutton/bundle.yaml', 'title: Purge')
        write(self.tmp, 'Add.pushbutton/script.py', 'pass')
        self.assertEqual([rel for rel, _lang in site.source_files(self.tmp)],
                         ['bundle.yaml',
                          'Add.pushbutton/script.py',
                          'Purge.pushbutton/script.py', 'Purge.pushbutton/bundle.yaml'])


class SourceSectionTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.tmp)
        self.bundle = os.path.join(self.tmp, 'Clash.panel', 'Clear_Clash.pushbutton')
        write(self.bundle, 'script.py', 'import pynavis  # the library\nx = "<gap>"\n')
        write(self.bundle, 'bundle.yaml', 'title: Clear Clash\n')
        self.tool = site.Tool('Clash', {'id': 'clear-clash', 'title': 'Clear Clash',
                                        'bundle': 'Clash.panel/Clear_Clash.pushbutton'}, '')

    def test_section_shows_each_file_highlighted_and_escaped(self):
        html = site.source_section(self.tool, extension=self.tmp)
        self.assertIn('<h2>Source</h2>', html)
        self.assertIn('<span class="tok-c"># the library</span>', html)
        self.assertIn('&lt;gap&gt;', html)
        self.assertNotIn('<gap>', html)
        self.assertIn('<span class="tok-k">title</span>', html)

    def test_each_file_links_to_its_github_page(self):
        html = site.source_section(self.tool, extension=self.tmp)
        self.assertIn('https://github.com/pynavis/pyNavis/blob/main/extensions/pyNavis.extension/'
                      'pyNavis.tab/Clash.panel/Clear_Clash.pushbutton/script.py', html)
        self.assertIn('<button class="copy-btn" type="button">Copy</button>', html)

    def test_tool_without_a_bundle_gets_no_section(self):
        tool = site.Tool('Clash', {'id': 'x', 'title': 'X'}, '')
        self.assertEqual(site.source_section(tool, extension=self.tmp), '')


if __name__ == '__main__':
    unittest.main()
