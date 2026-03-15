using System;
using System.Collections.Specialized;
using System.Reflection;
using Microsoft.Extensions.Options;
using VolumeAssistant.App;
using VolumeAssistant.App.Business;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class SourcePopupTests
    {
        [Fact]
        public void AppOptions_UseSourcePopup_DefaultsToTrue()
        {
            var opts = new AppOptions();
            Assert.True(opts.UseSourcePopup);
        }

        [Fact]
        public void OnLogEntriesChangedForPopup_ShowsPopupWhenConfigured()
        {
            var tray = new TrayApplication();

            // Create a test factory that records the text it was asked to show
            var recorded = new RecordedFactory();

            // Inject the test factory and app options via private fields
            var tType = typeof(TrayApplication);
            var factoryField = tType.GetField("_sourcePopupFactory", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var optionsField = tType.GetField("_appOptions", BindingFlags.NonPublic | BindingFlags.Instance)!;

            factoryField.SetValue(tray, recorded);
            optionsField.SetValue(tray, new AppOptions { UseSourcePopup = true });

            // Construct event args that signal an added log entry with a Source switched message
            var newItems = new string[] { "Source switched: PC → TV" };
            var args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems);

            // Invoke the private handler
            var handler = tType.GetMethod("OnLogEntriesChangedForPopup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            handler.Invoke(tray, new object?[] { null, args });

            Assert.Equal(1, recorded.Calls);
            Assert.Equal("TV", recorded.LastText);
        }

        private sealed class RecordedFactory : ISourcePopupFactory
        {
            public int Calls { get; private set; }
            public string? LastText { get; private set; }

            public ISourcePopup Create(string text)
            {
                Calls++;
                // The code passes the display text (after the arrow)
                LastText = text;
                return new TestPopup();
            }

            private sealed class TestPopup : ISourcePopup
            {
                public void ShowTemporary() { }
            }
        }
    }
}
