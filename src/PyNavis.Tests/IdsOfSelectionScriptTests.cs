using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The IDs of Selection bundle's pure reporting: what the toast says for
    /// one id, for many, and when some of the selection carries no id at all.
    /// Imported straight out of the shipped script.py, whose click behaviour
    /// sits behind "if '__commandpath__' in globals()".
    /// </summary>
    public class IdsOfSelectionScriptTests
    {
        private readonly IronPythonEngine _engine;

        public IdsOfSelectionScriptTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            config.SearchPaths.Add(Dir("lib"));
            config.SearchPaths.Add(Dir(Path.Combine(
                "pyNavis.tab", "04_Data.panel", "02_Element_IDs.stack", "02_IDs_of_Selection.pushbutton")));
            _engine.Initialize(config);
        }

        private static string Dir(string relative)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions", "pyNavis.extension", relative));
            if (!Directory.Exists(candidate))
                throw new DirectoryNotFoundException("not found at " + candidate);
            return candidate;
        }

        private void Run(string code)
        {
            var outw = new StringWriter();
            var r = _engine.Execute(new ScriptRequest
            {
                Code = "import script\n" + code + "\nprint('all tests passed')",
                Output = outw,
            });
            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("all tests passed", outw.ToString());
        }

        [Fact]
        public void Summary_OneId_NamesItInsteadOfCountingIt()
        {
            Run("level, message, detail = script.summary(['123456'], 0)\n" +
                "assert level == 'success', level\n" +
                "assert message == 'Element ID 123456 copied', message\n" +
                "assert detail is None, detail");
        }

        [Fact]
        public void Summary_SeveralIds_CountsThem()
        {
            Run("level, message, detail = script.summary(['1', '2', '3'], 0)\n" +
                "assert level == 'success', level\n" +
                "assert message == '3 element IDs copied', message\n" +
                "assert detail is None, detail");
        }

        [Fact]
        public void Summary_SomeItemsWithoutAnId_WarnsAndSaysHowMany()
        {
            Run("level, message, detail = script.summary(['1', '2', '3'], 2)\n" +
                "assert level == 'warning', level\n" +
                "assert message == '3 element IDs copied', message\n" +
                "assert detail == '2 selected item(s) carry no element ID.', detail");
        }

        [Fact]
        public void Summary_NothingResolved_ReportsThatRatherThanStayingSilent()
        {
            // A tool that finishes with no visible result reads as broken, so
            // the empty case gets its own toast.
            Run("level, message, detail = script.summary([], 4)\n" +
                "assert level == 'warning', level\n" +
                "assert message == 'No element IDs found', message\n" +
                "assert detail == 'None of the 4 selected item(s) carry an element ID.', detail");
        }

        // ---- id_of: which node up the chain the id is read from -------------

        /// <summary>
        /// A python stand-in for a ModelItem chain: just enough of
        /// PropertyCategories (iteration for props.categories, and
        /// FindPropertyByDisplayName for props.get) to drive id_of.
        /// </summary>
        private const string FakeNodes =
            "class Cat(object):\n" +
            "    def __init__(self, display, name):\n" +
            "        self.DisplayName, self.Name = display, name\n" +
            "class Val(object):\n" +
            "    def __init__(self, text):\n" +
            "        self._text = text\n" +
            "    def ToString(self):\n" +
            "        return self._text\n" +
            "class Prop(object):\n" +
            "    def __init__(self, text):\n" +
            "        self.Value = Val(text)\n" +
            "class Cats(object):\n" +
            "    def __init__(self, cats, props):\n" +
            "        self._cats, self._props = cats, props\n" +
            "    def __iter__(self):\n" +
            "        return iter(self._cats)\n" +
            "    def FindPropertyByDisplayName(self, category, prop):\n" +
            "        text = self._props.get((category, prop))\n" +
            "        return None if text is None else Prop(text)\n" +
            "class Node(object):\n" +
            "    def __init__(self, name, parent=None, element_id=None, is_element=False):\n" +
            "        self.DisplayName, self.Parent = name, parent\n" +
            "        cats = [Cat('Item', 'LcOaNode')]\n" +
            "        props = {}\n" +
            "        if is_element:\n" +
            "            cats.append(Cat('Element', 'LcRevitData_Element'))\n" +
            "        if element_id is not None:\n" +
            "            cats.append(Cat('Element ID', 'LcRevitId'))\n" +
            "            props[('Element ID', 'Value')] = element_id\n" +
            "        self.PropertyCategories = Cats(cats, props)\n" +
            "VALUES = {'id_category': 'Element ID', 'id_property': 'Value'}\n";

        [Fact]
        public void IdOf_ReportsTheNearestRealElement_NotTheNearestTab()
        {
            // The LF-S22D shape from the field: a click lands on a nested part
            // that carries the TYPE's id under "Element ID"; the instance two
            // levels up carries its own. The old rule copied 12345678 out and
            // Select by IDs then landed on a different fixture.
            Run(FakeNodes +
                "instance = Node('LF-S22D', None, '12345679', is_element=True)\n" +
                "middle = Node('S22D', instance)\n" +
                "part = Node('S22D', middle, '12345678')\n" +
                "assert script.id_of(part, VALUES) == '12345679', script.id_of(part, VALUES)\n" +
                "assert script.id_of(middle, VALUES) == '12345679'\n" +
                "assert script.id_of(instance, VALUES) == '12345679'");
        }

        [Fact]
        public void IdOf_FallsBackToTheFirstTab_WhenNothingIsMarkedAnElement()
        {
            // An exporter that never wrote the Element category still has to
            // yield an id, or the tool reports nothing for a perfectly good
            // selection.
            Run(FakeNodes +
                "root = Node('File', None)\n" +
                "wall = Node('Basic Wall', root, '11101054')\n" +
                "leaf = Node('Solid', wall)\n" +
                "assert script.id_of(leaf, VALUES) == '11101054', script.id_of(leaf, VALUES)");
        }

        [Fact]
        public void IdOf_StillReadsTheNameSuffix_OnADesignCoordinationModel()
        {
            // No Revit categories at all there; the bracketed name is the id.
            Run(FakeNodes +
                "root = Node('Model', None)\n" +
                "item = Node('Basic Wall [123456]', root)\n" +
                "assert script.id_of(item, VALUES) == '123456', script.id_of(item, VALUES)\n" +
                "assert script.id_of(root, VALUES) is None");
        }
    }
}
