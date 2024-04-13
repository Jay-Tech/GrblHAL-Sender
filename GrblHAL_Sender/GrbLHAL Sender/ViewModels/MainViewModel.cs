using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Transactions;
using ReactiveUI;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using GrbLHAL_Sender.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using GrbLHAL_Sender.Communication;
using System.Collections.ObjectModel;

namespace GrbLHAL_Sender.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly CommunicationManager _commManager;
    private  ObservableCollection<Axis> _axis;
    


    public bool IsJobRunning { get; set; }
    public bool HasATC { get; set; }
    public bool HasSdCard { get; set; }
    public bool HasProbing { get; set; }
    public bool IsFileLoaded { get; set; }
    public ICommand ConnectCommand { get; }
    public ICommand ZeroAxis { get; set; }
    public bool DisplayConnectionControl { get; set; }
    public Interaction<MainViewModel, ConnectionDialogViewModel?> ShowDialog { get; }
    
    public ICommand HomeCommand { get; }
    public bool Connected { get; }
    public Color HomeStateColor { get; }

    public ObservableCollection<Axis> AxisCollection
    {
        get => _axis;
        set => this.RaiseAndSetIfChanged(ref _axis, value);
        }


    public MainViewModel()
    {
        _commManager = new CommunicationManager();
        ConnectCommand = ReactiveCommand.Create(Connect);
        ZeroAxis = ReactiveCommand.Create<string>(Zero);
        ShowDialog = new Interaction<MainViewModel, ConnectionDialogViewModel?>();
        HomeCommand =  ReactiveCommand.Create(Home);

        _axis = new ObservableCollection<Axis>
        {
            new()
            {
                Name = "X",
                Position = 0.0001,
                ZeroWcsCommand  =  ZeroAxis
            },
            new()
            {
                Name = "Y",
                Position = 0.000,
                ZeroWcsCommand  =  ZeroAxis
            },
            new()
            {
                Name = "Z",
                Position = 0.0002,
                ZeroWcsCommand  =  ZeroAxis
            }
        };
        foreach (var axi in AxisCollection)
        {
            axi.Position = 0.00100;
        }
    }

    private void Zero(string axis)
    {

    }

    private void Home()
    {

    }

    private void Unlock()
    {

    }

    public void Connect()
    {
        //if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        //{
        //    var ownerWindow = desktopLifetime;
        //    var window = new ConnectionDialog();
        //    window.ShowDialog(ownerWindow);
        //}
        _commManager.NewConnection();
    }

}

public class Axis : ViewModelBase
{
    private double _position;
    public string? Name { get; set; }
    public ICommand? ZeroWcsCommand { get; set; }
    public double Position
    {
        get => _position;
        set => this.RaiseAndSetIfChanged(ref _position, value);
    }

    public Axis()
    {
        
    }

}