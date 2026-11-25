using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private SoundReactiveEngine? _soundEngine;
    private readonly SequencePlayer _sequencePlayer = new();
    private readonly Dictionary<PatchedFixture, HashSet<int>> _linkedHeadNumbers = new();
    private readonly Dictionary<PatchedFixture, HashSet<int>> _invertedHeadNumbers = new();
    private readonly Dictionary<ChannelDefinition, LinkableControl> _channelControls = new();
    private bool _isApplyingLinkedChange;

    /// <summary>One selectable target for the sound-reactive engine: a specific channel on a specific patched fixture.</summary>
    private sealed record SoundTargetOption(PatchedFixture Fixture, ChannelDefinition Channel)
    {
        public override string ToString() => $"{Fixture.Label} — {Channel.Name}";
    }

    /// <summary>
    /// Thin adapter so head-linking can read/write a channel's value
    /// without caring whether that channel is drawn as a plain Slider or
    /// a ratchet Knob -- both just need a gettable/settable numeric value
    /// and a range to invert against.
    /// </summary>
    private sealed record LinkableControl(Func<double> GetValue, Action<double> SetValue, double Minimum, double Maximum);

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
        Closing += (_, _) =>
        {
            _transmitLoop?.Dispose();
            _soundEngine?.Dispose();
            _sequencePlayer.Dispose();
        };
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
        RefreshSoundTargets();
    }

    /// <summary>Rebuilds the sound-reactive target list from every channel of every currently patched fixture.</summary>
    private void RefreshSoundTargets()
    {
        var previouslySelected = SoundTargetComboBox.SelectedItem as SoundTargetOption;

        var targets = _patch
            .SelectMany(fixture => fixture.Profile.Channels, (fixture, channel) => new SoundTargetOption(fixture, channel))
            .ToList();

        SoundTargetComboBox.ItemsSource = targets;

        var restored = targets.FirstOrDefault(t =>
            previouslySelected != null && t.Fixture == previouslySelected.Fixture && t.Channel == previouslySelected.Channel);
        SoundTargetComboBox.SelectedItem = restored ?? targets.FirstOrDefault();
    }

    /// <summary>Starts or stops the live sound-reactive engine, driving whichever channel is selected in SoundTargetComboBox.</summary>
    private void SoundReactiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_soundEngine is { IsRunning: true })
        {
            _soundEngine.Dispose();
            _soundEngine = null;
            SoundReactiveToggleButton.Content = "Start";
            SoundLevelBar.Value = 0;
            return;
        }

        if (SoundTargetComboBox.SelectedItem is not SoundTargetOption target)
        {
            MessageBox.Show("Patch a fixture and pick a target channel first.");
            return;
        }

        _soundEngine = new SoundReactiveEngine { Sensitivity = SensitivitySlider.Value };
        _soundEngine.LevelChanged += intensity => Dispatcher.BeginInvoke(() =>
        {
            SoundLevelBar.Value = intensity;

            if (target.Channel.Is16Bit)
                target.Fixture.SetChannel16(target.Channel.Offset, target.Channel.FineOffset!.Value, (ushort)(intensity << 8));
            else
                target.Fixture.SetChannel(target.Channel.Offset, intensity);
        });

        _soundEngine.Start();
        SoundReactiveToggleButton.Content = "Stop";
    }

    /// <summary>Live-adjustable while the engine is running -- no need to stop/start to retune.</summary>
    private void SensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SensitivityValueText.Text = $"{e.NewValue:0.0}x";

        if (_soundEngine is not null)
            _soundEngine.Sensitivity = e.NewValue;
    }

    /// <summary>
    /// Global shortcuts for faster testing: digits select a patched
    /// fixture, Space toggles sound-reactive, +/- nudge sensitivity, Del
    /// removes the selected fixture. Ignored while a text box has focus
    /// so typing a label or address still works normally.
    /// </summary>
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Space:
                SoundReactiveToggle_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.Delete:
                RemoveFixture_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case >= Key.D1 and <= Key.D9:
                var index = e.Key - Key.D1;
                if (index < PatchListBox.Items.Count)
                {
                    PatchListBox.SelectedIndex = index;
                    e.Handled = true;
                }
                break;

            case Key.OemPlus or Key.Add:
                SensitivitySlider.Value = Math.Min(SensitivitySlider.Maximum, SensitivitySlider.Value + 0.1);
                e.Handled = true;
                break;

            case Key.OemMinus or Key.Subtract:
                SensitivitySlider.Value = Math.Max(SensitivitySlider.Minimum, SensitivitySlider.Value - 0.1);
                e.Handled = true;
                break;

            case Key.N:
                SequenceNextStep_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.P:
                SequencePlayToggle_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Un-patches the selected fixture. Also zeroes its channel range in
    /// the universe on the way out, so stale values don't keep going out
    /// on the wire for addresses nothing is patched to anymore -- unless
    /// another patched fixture overlaps the same addresses, in which
    /// case that one's values win, same as always.
    /// </summary>
    private void RemoveFixture_Click(object sender, RoutedEventArgs e)
    {
        if (PatchListBox.SelectedItem is not PatchedFixture fixture)
        {
            MessageBox.Show("Select a patched fixture to remove first.");
            return;
        }

        for (var offset = 0; offset < fixture.Profile.ChannelCount; offset++)
            fixture.SetChannel(offset, 0);

        _patch.Remove(fixture);
        RefreshPatchList();

        SelectedFixtureHeader.Text = "No fixture selected";
        ChannelsPanel.ItemsSource = null;
    }

    private void PatchListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatchListBox.SelectedItem is not PatchedFixture fixture)
        {
            SelectedFixtureHeader.Text = "No fixture selected";
            ChannelsPanel.ItemsSource = null;
            LinkHeadsPanel.Children.Clear();
            return;
        }

        SelectedFixtureHeader.Text = $"{fixture.Label} — {fixture.Profile.Name} (start {fixture.StartAddress})";
        BuildHeadLinkControls(fixture);
        BuildChannelControls(fixture);
    }

    /// <summary>
    /// Builds a Link + Invert checkbox pair per head number found on the
    /// fixture (e.g. 1-4 on a Hydrabeam). Any subset of heads can be
    /// linked -- a pair, a triple, or all of them -- and moving any
    /// "Head N &lt;X&gt;" slider on a linked head mirrors the same value onto
    /// the matching channel on the other *linked* heads; unlinked heads
    /// stay independent. A linked head can also be marked Invert, so it
    /// moves to the opposite end of the range instead of mirroring exactly
    /// -- e.g. two linked heads where one is inverted means one spins
    /// clockwise while the other spins counter-clockwise on Pan. Shows
    /// nothing for fixtures with fewer than 2 heads (e.g. the Showtec par).
    /// </summary>
    private void BuildHeadLinkControls(PatchedFixture fixture)
    {
        LinkHeadsPanel.Children.Clear();

        var headNumbers = fixture.Profile.Channels
            .Select(c => GetHeadNumber(c.Name))
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        if (headNumbers.Count < 2)
            return;

        LinkHeadsPanel.Children.Add(new TextBlock
        {
            Text = "Link heads:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0)
        });

        var linked = _linkedHeadNumbers.TryGetValue(fixture, out var linkedSet) ? linkedSet : new HashSet<int>();
        var inverted = _invertedHeadNumbers.TryGetValue(fixture, out var invertedSet) ? invertedSet : new HashSet<int>();

        foreach (var headNumber in headNumbers)
        {
            var linkCheckBox = new CheckBox
            {
                Content = headNumber.ToString(),
                IsChecked = linked.Contains(headNumber),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var invertCheckBox = new CheckBox
            {
                Content = "inv",
                FontSize = 10,
                IsChecked = inverted.Contains(headNumber),
                IsEnabled = linked.Contains(headNumber),
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            linkCheckBox.Checked += (_, _) =>
            {
                SetHeadLinked(fixture, headNumber, true);
                invertCheckBox.IsEnabled = true;
            };
            linkCheckBox.Unchecked += (_, _) =>
            {
                SetHeadLinked(fixture, headNumber, false);
                invertCheckBox.IsEnabled = false;
                invertCheckBox.IsChecked = false;
            };

            invertCheckBox.Checked += (_, _) => SetHeadInverted(fixture, headNumber, true);
            invertCheckBox.Unchecked += (_, _) => SetHeadInverted(fixture, headNumber, false);

            LinkHeadsPanel.Children.Add(linkCheckBox);
            LinkHeadsPanel.Children.Add(invertCheckBox);
        }
    }

    private void SetHeadLinked(PatchedFixture fixture, int headNumber, bool linked)
    {
        if (!_linkedHeadNumbers.TryGetValue(fixture, out var set))
        {
            set = new HashSet<int>();
            _linkedHeadNumbers[fixture] = set;
        }

        if (linked)
            set.Add(headNumber);
        else
            set.Remove(headNumber);

        if (!linked && _invertedHeadNumbers.TryGetValue(fixture, out var invertedSet))
            invertedSet.Remove(headNumber);
    }

    private void SetHeadInverted(PatchedFixture fixture, int headNumber, bool inverted)
    {
        if (!_invertedHeadNumbers.TryGetValue(fixture, out var set))
        {
            set = new HashSet<int>();
            _invertedHeadNumbers[fixture] = set;
        }

        if (inverted)
            set.Add(headNumber);
        else
            set.Remove(headNumber);
    }

    /// <summary>Channel name suffix shared across heads, e.g. "Pan (8-bit)" for "Head 2 Pan (8-bit)". Null if the channel isn't a per-head one.</summary>
    private static string? GetHeadGroupKey(string channelName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(channelName, @"^Head \d+ (.+)$");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The head number a channel belongs to, e.g. 2 for "Head 2 Pan (8-bit)". Null if it isn't a per-head channel.</summary>
    private static int? GetHeadNumber(string channelName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(channelName, @"^Head (\d+) ");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    /// <summary>Reflects a value to the opposite end of a range -- e.g. Invert(60, 0, 255) = 195.</summary>
    private static double Invert(double value, double min, double max) => min + max - value;

    /// <summary>
    /// Mirrors a slider's new raw value onto the matching channel sliders
    /// of the fixture's other linked heads. If a linked head is marked
    /// Invert relative to the one being dragged, it's driven to the
    /// opposite end of the range instead of the same value -- so a pair
    /// with one inverted moves in opposite directions on that channel
    /// (e.g. Pan), while two heads that are both normal or both inverted
    /// still mirror each other exactly. Setting a target slider's Value
    /// fires its own ValueChanged, which updates that channel's value
    /// label and writes it to the universe the normal way -- this just
    /// drives those sliders, it doesn't touch the universe directly. The
    /// guard flag stops that from recursing back through here for every
    /// sibling it touches.
    /// </summary>
    private void PropagateLinkedValue(PatchedFixture fixture, ChannelDefinition sourceChannel, double rawValue)
    {
        if (_isApplyingLinkedChange)
            return;

        var sourceHead = GetHeadNumber(sourceChannel.Name);
        var suffix = GetHeadGroupKey(sourceChannel.Name);
        if (sourceHead is null || suffix is null)
            return;

        if (!_linkedHeadNumbers.TryGetValue(fixture, out var linkedHeads) || !linkedHeads.Contains(sourceHead.Value))
            return;

        if (!_channelControls.TryGetValue(sourceChannel, out var sourceControl))
            return;

        var inverted = _invertedHeadNumbers.TryGetValue(fixture, out var invertedSet) ? invertedSet : new HashSet<int>();

        // Normalize to a canonical "un-inverted" value first, so a drag on
        // an inverted head still propagates in the right direction to everyone else.
        var canonicalValue = inverted.Contains(sourceHead.Value)
            ? Invert(rawValue, sourceControl.Minimum, sourceControl.Maximum)
            : rawValue;

        _isApplyingLinkedChange = true;
        try
        {
            foreach (var channel in fixture.Profile.Channels)
            {
                if (channel == sourceChannel || GetHeadGroupKey(channel.Name) != suffix)
                    continue;

                var targetHead = GetHeadNumber(channel.Name);
                if (targetHead is null || !linkedHeads.Contains(targetHead.Value))
                    continue;

                if (!_channelControls.TryGetValue(channel, out var targetControl))
                    continue;

                var targetValue = inverted.Contains(targetHead.Value)
                    ? Invert(canonicalValue, targetControl.Minimum, targetControl.Maximum)
                    : canonicalValue;
                targetValue = Math.Clamp(targetValue, targetControl.Minimum, targetControl.Maximum);

                if (Math.Abs(targetControl.GetValue() - targetValue) > 0.5)
                    targetControl.SetValue(targetValue);
            }
        }
        finally
        {
            _isApplyingLinkedChange = false;
        }
    }

    /// <summary>Captures every channel's current value on the selected fixture as a new sequence step.</summary>
    private void CaptureStep_Click(object sender, RoutedEventArgs e)
    {
        if (PatchListBox.SelectedItem is not PatchedFixture fixture)
        {
            MessageBox.Show("Select a patched fixture to capture its current values.");
            return;
        }

        if (!double.TryParse(StepFadeSecondsTextBox.Text, out var fadeSeconds) || fadeSeconds < 0)
        {
            MessageBox.Show("Enter a valid fade time in seconds (0 or more; 0 = sharp cut).");
            return;
        }

        if (!double.TryParse(StepHoldSecondsTextBox.Text, out var holdSeconds) || holdSeconds < 0)
        {
            MessageBox.Show("Enter a valid hold time in seconds (0 or more).");
            return;
        }

        var values = fixture.Profile.Channels.ToDictionary(c => c, c => ReadChannelValue(fixture, c));

        var step = new SequenceStep
        {
            Fixture = fixture,
            ChannelValues = values,
            FadeSeconds = fadeSeconds,
            HoldSeconds = holdSeconds,
            Label = $"Step {_sequencePlayer.Steps.Count + 1}"
        };

        _sequencePlayer.AddStep(step);
        RefreshSequenceStepsList();
    }

    private static int ReadChannelValue(PatchedFixture fixture, ChannelDefinition channel) =>
        channel.Is16Bit ? fixture.GetChannel16(channel.Offset, channel.FineOffset!.Value) : fixture.GetChannel(channel.Offset);

    private void RefreshSequenceStepsList()
    {
        SequenceStepsListBox.ItemsSource = null;
        SequenceStepsListBox.ItemsSource = _sequencePlayer.Steps;
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (SequenceStepsListBox.SelectedItem is not SequenceStep step)
        {
            MessageBox.Show("Select a step to remove first.");
            return;
        }

        _sequencePlayer.RemoveStep(step);
        RefreshSequenceStepsList();
    }

    private void SequencePlayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_sequencePlayer.IsAutoPlaying)
        {
            _sequencePlayer.Stop();
            SequencePlayToggleButton.Content = "Play";
            return;
        }

        if (_sequencePlayer.Steps.Count == 0)
        {
            MessageBox.Show("Capture at least one step first.");
            return;
        }

        _sequencePlayer.Start(autoPlay: true);
        SequencePlayToggleButton.Content = "Pause";
    }

    private void SequenceNextStep_Click(object sender, RoutedEventArgs e)
    {
        if (_sequencePlayer.Steps.Count == 0)
        {
            MessageBox.Show("Capture at least one step first.");
            return;
        }

        if (!_sequencePlayer.IsRunning)
            _sequencePlayer.Start(autoPlay: false);

        _sequencePlayer.Next();
    }

    private void SequenceStop_Click(object sender, RoutedEventArgs e)
    {
        _sequencePlayer.Stop();
        SequencePlayToggleButton.Content = "Play";
    }

    private void BuildChannelControls(PatchedFixture fixture)
    {
        var rows = new List<UIElement>();
        _channelControls.Clear();

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
                MinWidth = 48,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };

            FrameworkElement control;

            if (channel.HasOptions)
            {
                var options = channel.Options!;
                var initialIndex = channel.OptionIndexForValue(fixture.GetChannel(channel.Offset));
                valueLabel.Text = options[initialIndex].Name;

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = options.Count - 1,
                    Width = 300,
                    Value = initialIndex,
                    VerticalAlignment = VerticalAlignment.Center,
                    TickFrequency = 1,
                    IsSnapToTickEnabled = true,
                    TickPlacement = TickPlacement.BottomRight
                };

                slider.ValueChanged += (_, args) =>
                {
                    var index = (int)Math.Round(args.NewValue);
                    var option = options[index];
                    fixture.SetChannel(channel.Offset, (byte)option.RepresentativeValue);
                    valueLabel.Text = option.Name;
                    PropagateLinkedValue(fixture, channel, index);
                };

                _channelControls[channel] = new LinkableControl(
                    () => slider.Value, v => slider.Value = v, slider.Minimum, slider.Maximum);
                control = slider;
            }
            else if (channel.Is16Bit)
            {
                var fineOffset = channel.FineOffset!.Value;
                var initial = fixture.GetChannel16(channel.Offset, fineOffset);
                valueLabel.Text = initial.ToString();

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 65535,
                    Width = 300,
                    Value = initial,
                    VerticalAlignment = VerticalAlignment.Center
                };

                slider.ValueChanged += (_, args) =>
                {
                    var value = (ushort)args.NewValue;
                    fixture.SetChannel16(channel.Offset, fineOffset, value);
                    valueLabel.Text = value.ToString();
                    PropagateLinkedValue(fixture, channel, value);
                };

                _channelControls[channel] = new LinkableControl(
                    () => slider.Value, v => slider.Value = v, slider.Minimum, slider.Maximum);
                control = slider;
            }
            else
            {
                var initial = fixture.GetChannel(channel.Offset);
                valueLabel.Text = initial.ToString();

                var slider = new Slider
                {
                    Minimum = 0,
                    Maximum = 255,
                    Width = 300,
                    Value = initial,
                    VerticalAlignment = VerticalAlignment.Center
                };

                slider.ValueChanged += (_, args) =>
                {
                    var value = (byte)args.NewValue;
                    fixture.SetChannel(channel.Offset, value);
                    valueLabel.Text = value.ToString();
                    PropagateLinkedValue(fixture, channel, value);
                };

                _channelControls[channel] = new LinkableControl(
                    () => slider.Value, v => slider.Value = v, slider.Minimum, slider.Maximum);
                control = slider;
            }

            row.Children.Add(nameLabel);
            row.Children.Add(control);
            row.Children.Add(valueLabel);
            rows.Add(row);
        }

        ChannelsPanel.ItemsSource = rows;
    }
}
