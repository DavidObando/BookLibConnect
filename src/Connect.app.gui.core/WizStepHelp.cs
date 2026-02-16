using System;
using System.Windows.Forms;

namespace Oahu.App.Gui {
  public partial class WizStepHelp : UserControl {

    private Action BtnAction { get; }

    public WizStepHelp (Action action)  {
      InitializeComponent ();
      BtnAction = action;
    }

    private void button1_Click (object sender, EventArgs e) {
      BtnAction ();
    }
  }
}
