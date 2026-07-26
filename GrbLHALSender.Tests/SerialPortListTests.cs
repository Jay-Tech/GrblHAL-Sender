using System.Collections.ObjectModel;
using GrbLHALSender.ViewModels;
using Xunit;

namespace GrbLHALSender.Tests;

/// <summary>
/// Tests for the serial port dropdown list. Two real symptoms motivate these: the
/// list showed the same COM port repeated several times (a USB CDC controller that
/// re-enumerates can leave several registry entries pointing at one COM name), and
/// refreshing used to Clear() the bound collection, which dropped the ComboBox
/// selection and let the two-way binding write that null back over the chosen port.
/// </summary>
public class SerialPortListTests
{
    [Fact]
    public void OrderPorts_RemovesDuplicates()
    {
        var ports = ConnectionViewModel.OrderPorts(
            ["COM6", "COM4", "COM4", "COM4", "COM4"]);

        Assert.Equal(["COM4", "COM6"], ports);
    }

    [Fact]
    public void OrderPorts_SortsNumerically()
    {
        // Plain string sorting puts COM11 and COM12 ahead of COM3, which is what
        // made the real list look shuffled.
        var ports = ConnectionViewModel.OrderPorts(
            ["COM6", "COM7", "COM8", "COM3", "COM11", "COM12", "COM4"]);

        Assert.Equal(["COM3", "COM4", "COM6", "COM7", "COM8", "COM11", "COM12"], ports);
    }

    [Fact]
    public void OrderPorts_HandlesNonWindowsNames()
    {
        var ports = ConnectionViewModel.OrderPorts(
            ["/dev/ttyUSB1", "/dev/ttyUSB0", "/dev/ttyACM0", " /dev/ttyACM0 ", ""]);

        Assert.Equal(["/dev/ttyACM0", "/dev/ttyUSB0", "/dev/ttyUSB1"], ports);
    }

    [Fact]
    public void ReconcilePorts_KeepsUnchangedEntriesInPlace()
    {
        var target = new ObservableCollection<string> { "COM3", "COM4" };
        var changed = false;
        target.CollectionChanged += (_, _) => changed = true;

        ConnectionViewModel.ReconcilePorts(target, ["COM3", "COM4"]);

        // No notification at all: the ComboBox keeps its selection untouched.
        Assert.False(changed);
        Assert.Equal(["COM3", "COM4"], target);
    }

    [Fact]
    public void ReconcilePorts_AddsAndRemovesInPlace()
    {
        var target = new ObservableCollection<string> { "COM3", "COM4", "COM9" };

        ConnectionViewModel.ReconcilePorts(target, ["COM3", "COM4", "COM11"]);

        Assert.Equal(["COM3", "COM4", "COM11"], target);
    }

    [Fact]
    public void ReconcilePorts_HealsAnAlreadyDuplicatedList()
    {
        // The state from the reported bug: real ports followed by repeated COM4.
        var target = new ObservableCollection<string>
        {
            "COM6", "COM7", "COM8", "COM3", "COM11", "COM12", "COM4", "COM4", "COM4", "COM4"
        };

        ConnectionViewModel.ReconcilePorts(
            target,
            ConnectionViewModel.OrderPorts(["COM6", "COM7", "COM8", "COM3", "COM11", "COM12", "COM4"]));

        Assert.Equal(["COM3", "COM4", "COM6", "COM7", "COM8", "COM11", "COM12"], target);
    }

    [Fact]
    public void ReconcilePorts_EmptiesWhenNoPortsRemain()
    {
        var target = new ObservableCollection<string> { "COM3", "COM4" };

        ConnectionViewModel.ReconcilePorts(target, []);

        Assert.Empty(target);
    }

    [Fact]
    public void ReconcilePorts_FillsAnEmptyList()
    {
        var target = new ObservableCollection<string>();

        ConnectionViewModel.ReconcilePorts(target, ["COM3", "COM4"]);

        Assert.Equal(["COM3", "COM4"], target);
    }
}
