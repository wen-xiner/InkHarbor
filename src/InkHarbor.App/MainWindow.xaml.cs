using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InkHarbor.App.ViewModels;
using InkHarbor.Core.Models;

namespace InkHarbor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        DataContext = _vm;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 流式输出时让滚动条跟着往下走，不然写完一屏就停住了。
        if (e.PropertyName == nameof(MainViewModel.OutputText))
        {
            OutputBox.ScrollToEnd();
        }
    }

    private void DraftList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            _vm.OpenDraft(e.AddedItems[0] as DraftItemViewModel);
        }
    }

    private void CorpusList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _vm.PreviewCorpus(CorpusList.SelectedItem as CorpusItemViewModel);
    }

    private void FinalList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _vm.OpenFinal(FinalList.SelectedItem as FinalFile);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.Dispose();
        base.OnClosed(e);
    }
}
