using Avalonia.Controls;

using System;
using SharpAmp.Avalonia.ViewModels;

namespace SharpAmp.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainWindowViewModel)?.Dispose();
            base.OnClosed(e);
        }
    }
}
