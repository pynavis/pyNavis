using System;
using System.IO;
using PyNavis.Runtime.Engine;
using Xunit;

namespace PyNavis.Tests
{
    /// <summary>
    /// The IronPython-to-C# delegate contract for the Clash Grouper.
    ///
    /// These exist because of a bug that reached the user's screen: the bundle
    /// script built its counts delegate as Func[int, TestCounts], and IronPython
    /// 3 maps the builtin int to BigInteger, not System.Int32, so the CLR
    /// rejected it with "expected Func[Int32, TestCounts], got
    /// Func[int, TestCounts]". Nothing caught it, because ShippedExtensionTests
    /// only compiles the script and the dialog's own tests build their delegates
    /// in C#, where int IS Int32. The generic argument has to be named as a .NET
    /// type from Python, and that is what is pinned here.
    /// </summary>
    public class ClashGrouperInteropTests
    {
        private readonly IronPythonEngine _engine;

        public ClashGrouperInteropTests()
        {
            _engine = new IronPythonEngine();
            var config = new EngineConfig();
            var stdlib = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(stdlib)) config.SearchPaths.Add(stdlib);
            config.SearchPaths.Add(PyNavisLibTests.PyNavisLibDir);
            _engine.Initialize(config);
        }

        private ExecResult Run(string code, TextWriter output = null)
        {
            return _engine.Execute(new ScriptRequest { Code = code, Output = output });
        }

        // Reaches Show through reflection rather than calling it: Show blocks on
        // ShowDialog, and the parameter types are the whole contract under test.
        private const string Preamble =
            "import clr\n" +
            "clr.AddReference('PyNavis.Runtime')\n" +
            "from System import Func, Int32\n" +
            "from PyNavis.Runtime.Forms import ClashGrouperDialog\n" +
            "TestCounts = ClashGrouperDialog.TestCounts\n" +
            "GrouperConfig = ClashGrouperDialog.GrouperConfig\n" +
            "PreviewData = ClashGrouperDialog.PreviewData\n" +
            "PreviewProgress = ClashGrouperDialog.PreviewProgress\n" +
            "params = clr.GetClrType(ClashGrouperDialog)"
            + ".GetMethod('Show').GetParameters()\n" +
            "wanted = dict((p.Name, p.ParameterType) for p in params)\n";

        [Fact]
        public void CountsDelegate_BuiltWithInt32_IsTheTypeShowExpects()
        {
            var outw = new StringWriter();
            var r = Run(Preamble +
                "built = Func[Int32, TestCounts]\n" +
                "print('match=%s' % (built == wanted['counts']))\n" +
                "print('wanted=%s' % wanted['counts'].FullName)",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("match=True", outw.ToString());
            Assert.Contains("System.Int32", outw.ToString());
        }

        [Fact]
        public void ProgressScope_IsImportable_AndCarriesTheMembersTheScriptCalls()
        {
            // ShippedExtensionTests only COMPILES bundle scripts, so a bad import
            // or a renamed member survives it - which is how the Func[int, ...]
            // bug reached a user. The write path calls exactly these four, and
            // apply_grouping's finally depends on Dispose existing.
            var outw = new StringWriter();
            var r = Run(
                "import clr\n" +
                "clr.AddReference('PyNavis.Runtime')\n" +
                "from PyNavis.Runtime.Forms import ProgressScope\n" +
                "for name in ('Begin', 'Report', 'Dispose', 'BarWidthFor', 'CloseAll'):\n" +
                "    print('%s=%s' % (name, hasattr(ProgressScope, name)))\n" +
                // Pure, so it needs no STA thread and no Navisworks host.
                "print('half=%s' % ProgressScope.BarWidthFor(0.5))\n" +
                "print('clamped=%s' % ProgressScope.BarWidthFor(2.0))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            var text = outw.ToString();
            foreach (var name in new[] { "Begin", "Report", "Dispose", "BarWidthFor", "CloseAll" })
                Assert.Contains(name + "=True", text);
            Assert.Contains("half=172", text);
            Assert.Contains("clamped=344", text);
        }

        [Fact]
        public void CountsDelegate_BuiltWithThePythonBuiltinInt_DoesNotMatch()
        {
            // The actual shipped bug, pinned so the fix cannot silently regress.
            // If a future IronPython maps int to Int32 this fails, which is the
            // right outcome: it means the guidance in the script's comment, and
            // this whole test, need revisiting.
            var outw = new StringWriter();
            var r = Run(Preamble +
                "print('match=%s' % (Func[int, TestCounts] == wanted['counts']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("match=False", outw.ToString());
        }

        /// <summary>
        /// Source-level, deliberately: the three tests above pin what the CLR
        /// wants, but they pass whatever the shipped script actually writes,
        /// because Show cannot be called from a test (it blocks on ShowDialog).
        /// This is the only mechanism left that fails when a bundle script
        /// reintroduces the builtin, and it covers every bundle, not just this
        /// one.
        /// </summary>
        [Fact]
        public void NoShippedScript_InstantiatesAClrGenericWithAPythonBuiltin()
        {
            var root = Path.GetFullPath(Path.Combine(
                PyNavisLibTests.PyNavisLibDir, "..", "extensions"));
            Assert.True(Directory.Exists(root), "extensions not found at " + root);

            // Func[int, X] / List[str] / Action[bool] and friends. The builtin
            // must be a whole word so Int32, Interval and similar do not match.
            var generic = new System.Text.RegularExpressions.Regex(
                @"\b(Func|Action|List|Dictionary|Predicate|Comparison|IList|IEnumerable)"
                + @"\[[^\]]*\b(int|float|bool|str)\b[^\]]*\]");

            var offenders = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.GetFiles(root, "*.py", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // Skip comments: the fix's own explanatory comment names the
                    // broken form on purpose.
                    var code = line.Split('#')[0];
                    if (generic.IsMatch(code))
                        offenders.Add(string.Format("{0}:{1}: {2}",
                            file.Substring(root.Length + 1), i + 1, line.Trim()));
                }
            }

            Assert.True(offenders.Count == 0,
                "IronPython 3 maps int/float/bool/str to BigInteger/double/bool/str, "
                + "not to System.Int32 and friends, so a CLR generic built with a "
                + "builtin is rejected at the call. Use System.Int32 etc:\n"
                + string.Join("\n", offenders));
        }

        [Fact]
        public void PreviewDelegate_IsTheTypeShowExpects()
        {
            var outw = new StringWriter();
            var r = Run(Preamble +
                "built = Func[GrouperConfig, PreviewProgress, PreviewData]\n" +
                "print('match=%s' % (built == wanted['preview']))",
                outw);

            Assert.True(r.Succeeded, r.ErrorText);
            Assert.Contains("match=True", outw.ToString());
        }
    }
}
