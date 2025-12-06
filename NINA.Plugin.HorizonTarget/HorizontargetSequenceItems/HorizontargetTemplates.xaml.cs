using System.ComponentModel.Composition;
using System.Windows;

namespace Fluxa.NINA.HorizonTarget.HorizontargetSequenceItems {
    [Export(typeof(ResourceDictionary))]
    public partial class HorizontargetTemplates : ResourceDictionary {
        public HorizontargetTemplates() {
            InitializeComponent();
        }
    }
}