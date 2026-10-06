using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace PyNavis.Cli
{
    /// <summary>
    /// What one <c>pynavis run</c> did, saved as results.json in its run folder. JSON keys
    /// are the lower-camel property names; each model's result is embedded as JSON, not text.
    /// </summary>
    public sealed class RunSummary
    {
        /// <summary>Full path of the script.</summary>
        public string Script { get; set; }

        /// <summary>The Navisworks year the run used.</summary>
        public string Year { get; set; }

        /// <summary>When the run started, ISO 8601.</summary>
        public string StartedAt { get; set; }

        /// <summary>When the run finished, ISO 8601.</summary>
        public string FinishedAt { get; set; }

        /// <summary>One outcome per model, in run order: a model Ctrl+C cut off is
        /// <c>cancelled</c>, and models the run never reached are <c>not-run</c>.</summary>
        public List<ModelOutcome> Models { get; } = new List<ModelOutcome>();

        /// <summary>Writes the summary as UTF-8 JSON, replacing any file at <paramref name="path"/>.</summary>
        public void Save(string path)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var json = new Dictionary<string, object>
            {
                ["script"] = Script,
                ["year"] = Year,
                ["startedAt"] = StartedAt,
                ["finishedAt"] = FinishedAt,
                ["models"] = Models.Select(m => new Dictionary<string, object>
                {
                    ["model"] = m.Model,
                    ["status"] = m.Status,
                    ["durationMs"] = m.DurationMs,
                    ["error"] = m.Error,
                    ["result"] = Embed(serializer, m.ResultJson),
                }).ToArray(),
            };
            File.WriteAllText(path, serializer.Serialize(json), new UTF8Encoding(false));
        }

        // Parsed back into objects so the serializer writes it as JSON, not as a string;
        // text that is not JSON (the add-in never writes any) is kept as the string.
        private static object Embed(JavaScriptSerializer serializer, string resultJson)
        {
            if (resultJson == null) return null;
            try { return serializer.DeserializeObject(resultJson); }
            catch (System.ArgumentException) { return resultJson; }
        }
    }

    /// <summary>How one model fared in a run.</summary>
    public sealed class ModelOutcome
    {
        /// <summary>Full path of the model, null for a run with no model.</summary>
        public string Model { get; set; }

        /// <summary><c>ok</c>, <c>failed</c>, <c>open-failed</c>, <c>save-failed</c>, <c>save-skipped</c>
        /// (the script was ok, but <c>--save</c> writes back only .nwd and .nwf files),
        /// <c>timed-out</c>, <c>cancelled</c> (Ctrl+C landed while it opened or its script ran)
        /// or <c>not-run</c> (the run ended before this model).</summary>
        public string Status { get; set; }

        /// <summary>How long the script took, in milliseconds.</summary>
        public long DurationMs { get; set; }

        /// <summary>The last line of what went wrong (a traceback's point), null when the model was ok.</summary>
        public string Error { get; set; }

        /// <summary>JSON text of the script's <c>__result__</c>, or null.</summary>
        public string ResultJson { get; set; }
    }
}
