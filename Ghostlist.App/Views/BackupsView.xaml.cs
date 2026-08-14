using System.Windows.Controls;
using System.Windows.Input;

namespace Ghostlist.App.Views;

public partial class BackupsView : UserControl
{
    public BackupsView() => InitializeComponent();

    private void Backups_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not BackupsViewModel model) return;
        if (model.RestoreCommand.CanExecute(null)) model.RestoreCommand.Execute(null);
    }
}
