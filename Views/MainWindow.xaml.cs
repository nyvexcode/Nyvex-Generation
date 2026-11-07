using System.Windows;
using NyvexGeneration.Helpers;
using NyvexGeneration.ViewModels;

namespace NyvexGeneration.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e) => WindowHelper.UseDarkTitleBar(this);
}
