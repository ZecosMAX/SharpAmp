using Avalonia.Controls;

using System;
using SharpAmp.Avalonia.ViewModels;

namespace SharpAmp.Avalonia.Views
{
    public partial class MainWindow : Window
    {
        private bool closeApproved;
        private bool checkingClose;
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (closeApproved || (DataContext as MainWindowViewModel)?.ProcessorChain.HasChanges != true) return;
            e.Cancel = true;
            if (checkingClose) return;
            checkingClose = true;
            try
            {
                if (await Editor.ConfirmDiscardAsync())
                {
                    closeApproved = true;
                    Close();
                }
            }
            finally { checkingClose = false; }
        }

        protected override void OnClosed(EventArgs e)
        {
            (DataContext as MainWindowViewModel)?.Dispose();
            base.OnClosed(e);
        }
    }
}
