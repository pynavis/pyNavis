using PyNavis.Runtime.Events;
using Xunit;

namespace PyNavis.Tests
{
    public class EventNamesTests
    {
        [Fact]
        public void Every_Event_RoundTrips_Through_Its_Name()
        {
            foreach (NavisEvent evt in System.Enum.GetValues(typeof(NavisEvent)))
            {
                var name = EventNames.NameOf(evt);
                Assert.Matches("^[a-z-]+$", name);
                Assert.True(EventNames.TryParse(name, out var back));
                Assert.Equal(evt, back);
            }
        }

        [Fact]
        public void TryParse_Is_CaseInsensitive_And_Rejects_Unknown()
        {
            Assert.True(EventNames.TryParse("Doc-Opened", out var evt));
            Assert.Equal(NavisEvent.DocOpened, evt);
            Assert.False(EventNames.TryParse("no-such-event", out _));
            Assert.False(EventNames.TryParse(null, out _));
        }

        [Fact]
        public void Args_Name_Matches_Event()
        {
            var args = new NavisEventArgs { Event = NavisEvent.SelectionChanged };
            Assert.Equal("selection-changed", args.Name);
        }

        [Fact]
        public void The_Two_Viewpoint_Movement_Events_Are_Nameable()
        {
            Assert.Equal("viewpoint-recalled", EventNames.NameOf(NavisEvent.ViewpointRecalled));
            Assert.Equal("camera-moved", EventNames.NameOf(NavisEvent.CameraMoved));
        }

        [Fact]
        public void Recalling_A_Viewpoint_Is_A_Different_Event_From_Editing_The_Tree()
        {
            // viewpoint-recalled (a saved view was activated) and viewpoints-changed (the
            // saved-viewpoints tree was edited) are one letter apart in the plural. A hook
            // author who picks the wrong file gets a script that never runs and no error,
            // so the two must stay distinct names for distinct events.
            Assert.True(EventNames.TryParse("viewpoint-recalled", out var recalled));
            Assert.True(EventNames.TryParse("viewpoints-changed", out var edited));
            Assert.Equal(NavisEvent.ViewpointRecalled, recalled);
            Assert.Equal(NavisEvent.ViewpointsChanged, edited);
            Assert.NotEqual(recalled, edited);
        }

        [Fact]
        public void Every_Event_Has_A_Name_Of_Its_Own()
        {
            // Two events sharing a wire name would make one of them unreachable as a hook
            // file, and TryParse would silently resolve it to the other.
            var names = new System.Collections.Generic.List<string>();
            foreach (NavisEvent evt in System.Enum.GetValues(typeof(NavisEvent)))
                names.Add(EventNames.NameOf(evt));

            Assert.Equal(names.Count, new System.Collections.Generic.HashSet<string>(names).Count);
        }

        [Fact]
        public void Losing_The_Current_Viewpoint_Is_Not_A_Recall()
        {
            // Navisworks raises CurrentSavedViewpointChanged when a view stops being
            // current too, with a null CurrentSavedViewpoint. Treating that as a recall
            // made orbiting away fire the hook, and made every click fire it twice: once
            // clearing the old view, once setting the new one.
            Assert.False(NavisEvents.IsRecall(null));
            Assert.False(NavisEvents.IsRecall(""));
            Assert.True(NavisEvents.IsRecall("Level 1"));
        }

        [Fact]
        public void Only_A_Recalled_Viewpoint_Carries_A_Viewpoint_Name()
        {
            var recalled = new NavisEventArgs
            {
                Event = NavisEvent.ViewpointRecalled,
                ViewpointName = "Level 2 - Overview",
            };
            Assert.Equal("Level 2 - Overview", recalled.ViewpointName);

            // Null everywhere else, exactly like CommandKey outside the command events.
            Assert.Null(new NavisEventArgs { Event = NavisEvent.CameraMoved }.ViewpointName);
        }
    }
}
