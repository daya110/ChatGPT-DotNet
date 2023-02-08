using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace ChatGPT.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(() =>
            {
                if (Application.Current is App application)
                {
                    application.ToggleAcrylicBlur();
                }
            })
        });

        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift),
            Command = new RelayCommand(() =>
            {
                if (Application.Current is App application)
                {
                    application.ToggleWindowState();
                }
            })
        });
    }
}
