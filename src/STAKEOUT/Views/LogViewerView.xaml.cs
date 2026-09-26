using System.Windows.Controls;

namespace Stakeout.Views;

public partial class LogViewerView : UserControl
{
    public LogViewerView() => InitializeComponent();

    /// <summary>Initial focus when the modal opens, so Tab cycles inside it.</summary>
    public void FocusDefault() => CloseButton.Focus();
}
