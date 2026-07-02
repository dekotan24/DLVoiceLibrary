using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using DLVoiceLibrary.ViewModels;

namespace DLVoiceLibrary.Views;

/// <summary>
/// Interaction logic for PlayerBar.xaml
/// </summary>
public partial class PlayerBar : UserControl
{
    private bool _isDraggingSeekSlider;

    public PlayerBar()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is PlayerBarViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is PlayerBarViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            SeekSlider.Value = newVm.Position.TotalSeconds;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDraggingSeekSlider) return;
        if (e.PropertyName != nameof(PlayerBarViewModel.Position)) return;
        if (sender is not PlayerBarViewModel vm) return;

        SeekSlider.Value = vm.Position.TotalSeconds;
    }

    private void OnSeekSliderPreviewMouseDown(object sender, MouseButtonEventArgs e) => _isDraggingSeekSlider = true;

    private void OnSeekSliderPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSeekSlider = false;
        if (DataContext is PlayerBarViewModel vm)
        {
            vm.SeekCommand.Execute(SeekSlider.Value);
        }
    }

    private void OnSleepTimerClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not PlayerBarViewModel vm) return;

        var dialog = new SleepTimerDialog(vm) { Owner = System.Windows.Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
