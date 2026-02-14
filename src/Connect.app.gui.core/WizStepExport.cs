using System;
using System.Windows.Forms;
using BookLibConnect.Aux;
using BookLibConnect.Aux.Win;
using BookLibConnect.Core;

namespace BookLibConnect.App.Gui {
  public partial class WizStepExport : UserControl, ICompleted {
    public event EventHandler Completed;
    
    private Func<bool> BtnAction { get; }
    private ExportSettings Settings { get; }
    private bool _ignoreFlag;

    public WizStepExport (Func<bool> btnAction, ExportSettings settings)  {
      
      InitializeComponent ();
      BtnAction = btnAction;
      Settings = settings;
      
      using var _ = new ResourceGuard (x => _ignoreFlag = x);
      button1.Enabled = false;

      if (Settings.ExportToAax.HasValue) {
        checkBox1.Checked = Settings.ExportToAax.Value;
      }
    }

    private void checkBox1_CheckedChanged (object sender, EventArgs e) {
      if (checkBox1.CheckState != CheckState.Indeterminate)
        Settings.ExportToAax = checkBox1.Checked;
      enable ();
    }

    private void button1_Click (object sender, EventArgs e) {
      BtnAction ();
      enable ();
    }

    private void enable () {
      if (_ignoreFlag)
        return;
      button1.Enabled = Settings.ExportToAax ?? false;
      bool succ = Settings.ExportToAax.HasValue && 
        (!Settings.ExportToAax.Value || Settings.ExportDirectory is not null);
      if (succ)
        Completed?.Invoke (this, EventArgs.Empty);

    }
  }
}
