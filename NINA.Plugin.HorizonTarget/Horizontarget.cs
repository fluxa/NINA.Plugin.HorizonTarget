using Fluxa.NINA.HorizonTarget.Properties;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Settings = Fluxa.NINA.HorizonTarget.Properties.Settings;

namespace Fluxa.NINA.HorizonTarget {
    /// <summary>
    /// This class exports the IPluginManifest interface and will be used for the general plugin information and options
    /// The base class "PluginBase" will populate all the necessary Manifest Meta Data out of the AssemblyInfo attributes.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class Horizontarget : PluginBase {
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public Horizontarget(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            this.profileService = profileService;

            // Register sequence item templates
            var sequenceTemplates = new System.Windows.ResourceDictionary();
            sequenceTemplates.Source = new Uri(
                "Fluxa.NINA.HorizonTarget;component/HorizontargetSequenceItems/HorizontargetTemplates.xaml",
                UriKind.RelativeOrAbsolute
            );
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(sequenceTemplates);
        }

        public override Task Teardown() {
            return base.Teardown();
        }
    }
}
