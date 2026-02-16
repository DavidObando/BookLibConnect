using System;
using System.Windows.Forms;
using Oahu.Aux.Win;
using Oahu.Core;
using Oahu.Core.UI;

namespace Oahu.App.Gui {
  public partial class WizStepProfile : UserControl, ICompleted {
    public event EventHandler Completed;
    
    private AudibleClient Client { get; }

    public WizStepProfile (AudibleClient client)  {
      InitializeComponent ();
      Client = client;
    }

    private void button1_Click (object sender, EventArgs e) {
      var dlg = new NewProfileForm (Client, null);
      var result = dlg.ShowDialog ();

      bool succ = result == DialogResult.OK && dlg.ProfileKey is not null;
      if (succ) {
        button1.Enabled = false;
        Completed?.Invoke (this, EventArgs.Empty);
      }
    }
  }
}
