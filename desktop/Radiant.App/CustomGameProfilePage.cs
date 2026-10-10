using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Radiant.Games;

namespace Radiant.App;

public sealed partial class MainWindow
{
    private Border CustomProfileEditor(GameId game, GameConfigFile? file, CancellationToken token)
    {
        var body = Ui.Stack(Ui.Text("YOUR SETTINGS / PREMIUM", 11, Ui.Accent, true),
            Ui.Text("Build a " + GameName(game) + " profile.", 22, bold: true),
            Ui.Text("Change only what matters to you. Save a named setup and load it for your next review. Each application backs up the current game file.", 13, Ui.Muted));
        var store = new SavedGameProfiles(Path.Combine(dataDirectory, "saved-game-profiles"));
        var controls = new Dictionary<string, ComboBox>();
        var preview = Ui.Stack();
        var fields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 10) };
        void Add(string key, string label, IEnumerable<(string Label, int Value)> options)
        {
            var choice = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 170,
                Margin = new Thickness(0, 0, 16, 12) };
            choice.Items.Add(new ComboBoxItem { Content = "Keep current", Tag = null });
            foreach (var option in options) choice.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            choice.SelectedIndex = 0; choice.SelectionChanged += (_, _) => preview.Children.Clear(); controls[key] = choice;
            fields.Children.Add(Ui.Stack(Ui.Text(label, 12, Ui.Muted), choice));
        }
        if (game != GameId.Valorant) Add("vsync", "VSync", [("Off", 0), ("On", 1)]);
        if (game == GameId.CounterStrike2) Add("msaa", "Multisample anti-aliasing", [("Off", 0), ("2× MSAA", 2), ("4× MSAA", 4), ("8× MSAA", 8)]);
        else Add("cap", "FPS limit", new[] { 30, 60, 75, 90, 120, 144, 165, 175, 180, 200, 240, 280, 300, 360, 480, 500 }
            .Select(value => (value + " FPS", value)));
        (string, int)[] quality = [("Low · 0", 0), ("Medium · 1", 1), ("High · 2", 2), ("Epic · 3", 3)];
        if (game == GameId.Fortnite)
            foreach (var field in new[] { ("shadow", "Shadows"), ("texture", "Textures"), ("effects", "Effects"), ("post", "Post processing"), ("foliage", "Foliage") })
                Add(field.Item1, field.Item2, quality);
        if (game == GameId.Valorant)
        {
            Add("material", "Material quality", quality.Take(3)); Add("detail", "Detail quality", quality.Take(3));
        }
        body.Children.Add(fields);
        body.Children.Add(Ui.Text(game == GameId.CounterStrike2
            ? "VSync can reduce tearing but can add latency. MSAA improves edges at a GPU cost. CS2 frame caps are not changed by this adapter."
            : "Choose a sustainable FPS cap. Lower quality can reduce GPU work; higher quality adds detail. Absent optional settings are left absent.", 12, Ui.Muted));
        int? Pick(string key) => controls.TryGetValue(key, out var input) && input.SelectedItem is ComboBoxItem { Tag: int value } ? value : null;
        CustomGameProfile Read() => new(game, Pick("vsync") is int sync ? sync == 1 : null, Pick("msaa"), Pick("cap"),
            Pick("shadow"), Pick("texture"), Pick("effects"), Pick("post"), Pick("foliage"), Pick("material"), Pick("detail"));
        void Set(string key, int? value)
        {
            if (!controls.TryGetValue(key, out var input)) return;
            if (value is not null && !input.Items.Cast<ComboBoxItem>().Any(i => Equals(i.Tag, value)))
                input.Items.Add(new ComboBoxItem { Content = value.Value.ToString(CultureInfo.InvariantCulture), Tag = value.Value });
            input.SelectedItem = input.Items.Cast<ComboBoxItem>().Single(i => Equals(i.Tag, value));
        }
        var name = new TextBox { Text = "My " + GameName(game) + " setup", MaxLength = 60, MinWidth = 220,
            Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(8) };
        var saved = new ComboBox { MinWidth = 250, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 12) };
        void LoadList()
        {
            saved.Items.Clear();
            foreach (var item in store.List(game)) saved.Items.Add(new ComboBoxItem { Content = item.Name + " · " + item.CreatedAt.ToLocalTime().ToString("MMM d, HH:mm"), Tag = item });
            if (saved.Items.Count == 0) saved.Items.Add(new ComboBoxItem { Content = "No saved profiles yet", IsEnabled = false });
            saved.SelectedIndex = 0;
        }
        try { LoadList(); } catch (Exception error) { body.Children.Add(Ui.Text(error.Message, 12, Ui.Brush("#F0AE7C"))); }
        var save = AsyncButton("Save as new profile · Premium", async () =>
        {
            if (!licenses.CachedStatus().IsPremium) { await NavigateAsync("account"); return; }
            token.ThrowIfCancellationRequested();
            var result = store.Save(name.Text, Read()); LoadList(); Notice("Saved “" + result.Name + "”. Game settings have not changed.");
        });
        body.Children.Add(Ui.Text("PROFILE NAME", 10, Ui.Muted, true)); body.Children.Add(name); body.Children.Add(save);
        body.Children.Add(saved);
        body.Children.Add(AsyncButton("Load saved settings", () =>
        {
            if (saved.SelectedItem is not ComboBoxItem { Tag: SavedGameProfile item }) return Task.CompletedTask;
            var p = CustomGameProfiles.Decode(item.PresetId); name.Text = item.Name;
            Set("vsync", p.VSync is bool vsync ? vsync ? 1 : 0 : null); Set("msaa", p.Msaa); Set("cap", p.FrameCap);
            Set("shadow", p.ShadowQuality); Set("texture", p.TextureQuality); Set("effects", p.EffectsQuality);
            Set("post", p.PostProcessQuality); Set("foliage", p.FoliageQuality); Set("material", p.MaterialQuality); Set("detail", p.DetailQuality);
            preview.Children.Clear(); Notice("Saved settings loaded. Preview before applying."); return Task.CompletedTask;
        }));
        if (file is not null)
            body.Children.Add(AsyncButton("Preview my changes", () => PreviewGameProfileAsync(file, CustomGameProfiles.Encode(Read()),
                name.Text, "Only your selected settings will change. The exact current file is backed up before applying.", preview, token), true));
        else body.Children.Add(Ui.Text("You can save a setup now. To preview and apply it, first save video settings in the game, close it, then scan or choose its configuration file above.", 13, Ui.Muted));
        body.Children.Add(preview); return Ui.Card(body, 18);
    }
}
