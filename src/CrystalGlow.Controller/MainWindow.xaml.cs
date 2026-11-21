using System.IO;
using System.Windows;
using System.Windows.Controls;
using CrystalGlow.Dmx;

namespace CrystalGlow.Controller;

/// <summary>
/// Prototype patch + channel UI. Loads fixture profiles from the Profiles
/// folder, lets you patch instances of them at a start address, and shows
/// a slider per channel for whichever fixture is selected.
///
/// There is no hardware output wired up yet — sliders only edit the
/// in-memory DmxUniverse buffer. That's the next piece to add once a
/// transport (USB dongle / Art-Net / sACN) is chosen.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DmxUniverse _universe = new();
    private readonly List<FixtureProfile> _profiles;
    private readonly List<PatchedFixture> _patch = new();
    private DmxTransmitLoop? _transmitLoop;
    private bool _transportComboInitializing;

    /// <summary>
    /// Which COM port the USB-DMX dongle enumerates as. Check Device
    /// Manager -- it'll change if you plug it into a different USB port
    /// or add/remove other serial devices.
    /// </summary>
    private const string DongleComPort = "COM6";

    /// <summary>
    /// Two competing theories for what this dongle actually expects, both
    /// unverified against real hardware -- untangle this by just picking
    /// from the dropdown, no rebuild needed:
    ///
    /// RawBitBang: treats it as a bare Open-DMX-style dongle -- the host
    /// generates real DMX512 timing (break/MAB/250kbit) itself.
    ///
    /// EnttecFramed: treats it as having its own onboard microcontroller
    /// that does the DMX timing, and just wants a simple framed packet
    /// over an ordinary serial link (the Enttec DMX USB PRO protocol,
    /// widely cloned by budget dongles).
    /// </summary>
    private enum TransportKind
    {
        RawBitBang,
        EnttecFramed,
    }

    private sealed record TransportOption(TransportKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly TransportOption[] TransportOptions =
    [
        new(TransportKind.RawBitBang, "Raw bit-bang (Open DMX style)"),
        new(TransportKind.EnttecFramed, "Enttec-framed packets"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        var profilesDir = Path.Combine(AppContext.BaseDirectory, "Profiles");
        _profiles = FixtureProfileLoader.LoadFromFolder(profilesDir);

        ProfileComboBox.ItemsSource = _profiles;
        if (_profiles.Count > 0)
            ProfileComboBox.SelectedIndex = 0;

        if (_profiles.Count == 0)
        {
            MessageBox.Show(
                $"No fixture profiles found in:\n{profilesDir}\n\nAdd .json profile files there.",
                "No profiles loaded");
        }

        _transportComboInitializing = true;
        TransportComboBox.ItemsSource = TransportOptions;
        TransportComboBox.SelectedIndex = 0;
        _transportComboInitializing = false;

        StartOutput(TransportOptions[0].Kind);
        Closing += (_, _) => _transmitLoop?.Dispose();
    }

    private void TransportComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_transportComboInitializing)
            return;

        if (TransportComboBox.SelectedItem is TransportOption option)
            StartOutput(option.Kind);
    }

    /// <summary>
    /// Tries to open the USB-DMX dongle using the given transport. If
    /// that fails for any reason -- not plugged in, wrong port number,
    /// port in use by something else -- falls back to a no-op output so
    /// the rest of the app keeps working for patching/testing without
    /// hardware. Replaces whatever transport was running before, so you
    /// can switch live from the dropdown without restarting the app.
    /// </summary>
    private void StartOutput(TransportKind kind)
    {
        _transmitLoop?.Dispose();

        IDmxOutput output;
        string status;

        try
        {
            output = kind switch
            {
                TransportKind.EnttecFramed => new EnttecProtocolDmxOutput(DongleComPort),
                _ => new SerialDmxOutput(DongleComPort),
            };
            status = $"Output: {DongleComPort} (live, {kind})";
        }
        catch (Exception ex)
        {
            output = new NullDmxOutput();
            status = $"Output: none -- {ex.Message} (sliders only affect the in-memory buffer)";
        }

        OutputStatusText.Text = status;

        _transmitLoop = new DmxTransmitLoop(_universe, output);
        _transmitLoop.SendFailed += ex =>
            Dispatcher.Invoke(() => OutputStatusText.Text = $"Output error: {ex.Message}");
        _transmitLoop.Start();
    }

    private void AddFixture_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileComboBox.SelectedItem is not FixtureProfile profile)
        {
            MessageBox.Show("Select a fixture profile first.");
            return;
        }

        if (!int.TryParse(StartAddressTextBox.Text, out int startAddress) || startAddress < 1 || startAddress > 512)
        {
            MessageBox.Show("Enter a valid start address (1-512).");
            return;
        }

        if (startAddress + profile.ChannelCount - 1 > 512)
        {
            MessageBox.Show("This fixture's channels would run past address 512. Pick a lower start address.");
            return;
        }

        var label = string.IsNullOrWhiteSpace(LabelTextBox.Text)
            ? $"Fixture {_patch.Count + 1}"
            : LabelTextBox.Text.Trim();

        var fixture = new PatchedFixture(_universe, profile, startAddress, label);
        _patch.Add(fixture);
        RefreshPatchList();
    }

    private void RefreshPatchList()
    {
        PatchListBox.ItemsSource = null;
        PatchListBox.ItemsSource = _patch;
    }

    private void PatchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatchListBox.SelectedItem is not PatchedFixture fixture)
        {
            SelectedFixtureHeader.Text = "No fixture selected";
            ChannelsPanel.ItemsSource = null;
            return;
        }

        SelectedFixtureHeader.Text = $"{fixture.Label} — {fixture.Profile.Name} (start {fixture.StartAddress})";
        BuildChannelControls(fixture);
    }

    private void BuildChannelControls(PatchedFixture fixture)
    {
        var rows = new List<UIElement>();

        foreach (var channel in fixture.Profile.Channels.OrderBy(c => c.Offset))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

            var nameLabel = new TextBlock
            {
                Text = channel.Name,
                Width = 100,
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueLabel = new TextBlock
            {
                Width = 40,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Text = fixture.GetChannel(channel.Offset).ToString()
            };

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 255,
                Width = 300,
                Value = fixture.GetChannel(channel.Offset),
                VerticalAlignment = VerticalAlignment.Center
            };

            slider.ValueChanged += (_, args) =>
            {
                var value = (byte)args.NewValue;
                fixture.SetChannel(channel.Offset, value);
                valueLabel.Text = value.ToString();
            };

            row.Children.Add(nameLabel);
            row.Children.Add(slider);
            row.Children.Add(valueLabel);
            rows.Add(row);
        }

        ChannelsPanel.ItemsSource = rows;
    }
}
