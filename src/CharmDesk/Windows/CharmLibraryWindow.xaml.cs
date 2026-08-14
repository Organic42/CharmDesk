using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CharmDesk.Core;
using CharmDesk.Persistence;
using Microsoft.Win32;

namespace CharmDesk.Windows;

/// <summary>
/// Grid of installed charm packages, styled as a pegboard of acrylic charm tags - the same way
/// a keychain shop pegs its stock up for browsing. Selecting one immediately makes it the
/// active desktop charm; Preview opens a standalone physics playground; Edit hands off to the
/// manager. The last tile is always a dashed "new pin" slot that opens the importer.
/// </summary>
public partial class CharmLibraryWindow : Window
{
    private readonly App _app;

    // A charm's category picks a tag color from this set (stable per string, not per instance),
    // so the collection reads with a bit of the color variety of a real pin board without any
    // per-charm color authoring.
    private static readonly (Color Bg, Color Fg)[] TagPalette =
    {
        (Color.FromRgb(0xFB, 0xDD, 0x9E), Color.FromRgb(0x6B, 0x42, 0x00)), // gold
        (Color.FromRgb(0xFF, 0xC9, 0xE3), Color.FromRgb(0x94, 0x0A, 0x49)), // pink
        (Color.FromRgb(0xB9, 0xEE, 0xDC), Color.FromRgb(0x08, 0x5C, 0x47)), // mint
        (Color.FromRgb(0xD8, 0xC5, 0xF9), Color.FromRgb(0x3F, 0x1D, 0x8C)), // lilac
    };

    public CharmLibraryWindow(App app)
    {
        _app = app;
        InitializeComponent();
        Activated += (_, _) => RebuildCards();
        RebuildCards();
    }

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        var activeId = _app.Settings.Current.SelectedCharmId;

        foreach (var package in _app.Registry.LoadAll())
        {
            try
            {
                CardsPanel.Children.Add(BuildCard(package, isActive:
                    string.Equals(package.Manifest.Id, activeId, StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex)
            {
                // One bad charm (unreadable image, etc.) should never blank out the rest of
                // the shelf or the "New Pin" slot after it.
                CharmDesk.Persistence.Logger.Log($"CharmLibraryWindow.BuildCard ({package.Manifest.Id})", ex);
            }
        }

        CardsPanel.Children.Add(BuildAddCard());
    }

    private static (Color Bg, Color Fg) TagColorFor(string category)
    {
        var hash = 0;
        foreach (var c in category) hash = hash * 31 + c;
        var index = Math.Abs(hash) % TagPalette.Length;
        return TagPalette[index];
    }

    private FrameworkElement BuildCard(CharmPackage package, bool isActive)
    {
        var manifest = package.Manifest;
        var lineBrush = (Brush)FindResource("CardLineBrush");
        var mintBrush = (Brush)FindResource("MintBrush");
        var accentBrush = (Brush)FindResource("AccentBrush");
        var bgBrush = (Brush)FindResource("BgBrush");
        var ringBrush = isActive ? mintBrush : lineBrush;

        var content = new StackPanel { Margin = new Thickness(14, 16, 14, 14) };

        // Category tag chip, colored per-category so the shelf reads with some variety.
        var (tagBg, tagFg) = TagColorFor(manifest.Category);
        var tag = new Border
        {
            Background = new SolidColorBrush(tagBg),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = manifest.Enabled ? 1.0 : 0.5,
            Child = new TextBlock
            {
                Text = manifest.Category.ToUpperInvariant(),
                FontFamily = (FontFamily)FindResource("FontMono"),
                FontSize = 11,
                Foreground = new SolidColorBrush(tagFg),
            },
        };

        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Height = 116,
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = manifest.Enabled ? 1.0 : 0.4,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var thumbPath = File.Exists(package.ThumbnailPath) ? package.ThumbnailPath : package.ImagePath;
        if (File.Exists(thumbPath))
        {
            image.Source = ImageLoader.TryLoad(thumbPath, "CharmLibraryWindow.BuildCard");
        }

        var nameText = new TextBlock
        {
            Text = manifest.Name,
            FontFamily = (FontFamily)FindResource("FontDisplaySemi"),
            FontSize = 17,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var primary = new Button
        {
            Content = isActive ? "On desk ✓" : "Hang this",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Background = isActive ? mintBrush : accentBrush,
            Foreground = Brushes.White,
            BorderBrush = lineBrush,
        };
        primary.Click += (_, _) => { _app.ActivateCharm(manifest.Id); RebuildCards(); };

        var previewButton = new Button { Content = "\U0001F441 Preview", Style = (Style)FindResource("IconButton"), Margin = new Thickness(0, 0, 4, 0) };
        previewButton.Click += (_, _) => new CharmPreviewWindow(package) { Owner = this }.Show();

        var editButton = new Button { Content = "✎ Edit", Style = (Style)FindResource("IconButton"), Margin = new Thickness(0, 0, 4, 0) };
        editButton.Click += (_, _) =>
        {
            var manager = new CharmManagerWindow(_app, package) { Owner = this };
            manager.ShowDialog();
            RebuildCards();
        };

        var exportButton = new Button { Content = "⤓", Style = (Style)FindResource("IconButton"), ToolTip = "Export as a .zip charm pack" };
        exportButton.Click += (_, _) => ExportPack(package);

        var secondaryRow = new Grid();
        secondaryRow.ColumnDefinitions.Add(new ColumnDefinition());
        secondaryRow.ColumnDefinitions.Add(new ColumnDefinition());
        secondaryRow.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(previewButton, 0);
        Grid.SetColumn(editButton, 1);
        Grid.SetColumn(exportButton, 2);
        previewButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        editButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        exportButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        secondaryRow.Children.Add(previewButton);
        secondaryRow.Children.Add(editButton);
        secondaryRow.Children.Add(exportButton);

        content.Children.Add(tag);
        content.Children.Add(image);
        content.Children.Add(nameText);
        content.Children.Add(primary);
        content.Children.Add(secondaryRow);

        var card = new Border
        {
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = ringBrush,
            BorderThickness = new Thickness(isActive ? 2.4 : 1.8),
            CornerRadius = new CornerRadius(18),
            Width = 200,
            Child = content,
            Effect = new DropShadowEffect
            {
                Color = ((SolidColorBrush)lineBrush).Color,
                Opacity = 0.14,
                BlurRadius = 0,
                ShadowDepth = 4,
                Direction = 270,
            },
        };

        var hole = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = bgBrush,
            Stroke = ringBrush,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, -9, 0, 0),
        };

        var wrapper = new Grid { Margin = new Thickness(0, 0, 16, 18) };
        var lift = new TranslateTransform();
        wrapper.RenderTransform = lift;
        wrapper.Children.Add(card);
        wrapper.Children.Add(hole);
        AttachHoverLift(wrapper, lift);

        return wrapper;
    }

    private FrameworkElement BuildAddCard()
    {
        var lineBrush = (Brush)FindResource("Accent2Brush");
        var bgBrush = (Brush)FindResource("BgBrush");
        var tintBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEE, 0xF6));

        var plus = new TextBlock
        {
            Text = "+",
            FontFamily = (FontFamily)FindResource("FontDisplay"),
            FontSize = 42,
            Foreground = lineBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var label = new TextBlock
        {
            Text = "NEW PIN",
            FontFamily = (FontFamily)FindResource("FontMono"),
            FontSize = 12,
            Foreground = lineBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        content.Children.Add(plus);
        content.Children.Add(label);

        var card = new Border
        {
            Background = tintBrush,
            BorderBrush = lineBrush,
            BorderThickness = new Thickness(2.6),
            CornerRadius = new CornerRadius(18),
            Width = 200,
            Height = 268,
            Child = content,
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        card.MouseLeftButtonUp += (_, _) =>
        {
            var manager = new CharmManagerWindow(_app) { Owner = this };
            manager.ShowDialog();
            RebuildCards();
        };

        var hole = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = bgBrush,
            Stroke = lineBrush,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, -9, 0, 0),
        };

        var wrapper = new Grid { Margin = new Thickness(0, 0, 16, 18) };
        var lift = new TranslateTransform();
        wrapper.RenderTransform = lift;
        wrapper.Children.Add(card);
        wrapper.Children.Add(hole);
        AttachHoverLift(wrapper, lift);

        return wrapper;
    }

    private static void AttachHoverLift(UIElement element, TranslateTransform transform)
    {
        element.MouseEnter += (_, _) => Animate(transform, -4);
        element.MouseLeave += (_, _) => Animate(transform, 0);

        static void Animate(TranslateTransform t, double to)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            t.BeginAnimation(TranslateTransform.YProperty, anim);
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e) =>
        new AboutWindow(_app.Registry) { Owner = this }.ShowDialog();

    // ---- Export / drag-drop import -----------------------------------------

    private void ExportPack(CharmPackage package)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export charm pack",
            Filter = "Charm pack (*.zip)|*.zip",
            FileName = $"{package.Manifest.Id}.zip",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            _app.Registry.ExportPack(package, dlg.FileName);
            MessageBox.Show(this, $"Exported '{package.Manifest.Name}' to {dlg.FileName}", "CharmDesk",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Log("CharmLibraryWindow.ExportPack", ex);
            MessageBox.Show(this, $"Couldn't export this charm: {ex.Message}", "CharmDesk",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        var file = files.FirstOrDefault(f =>
            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (file is null) return;

        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                _app.Registry.ImportPack(file);
                _app.RefreshAfterLibraryChange();
                RebuildCards();
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Logger.Log("CharmLibraryWindow.Drop (zip)", ex);
                MessageBox.Show(this, ex.Message, "CharmDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            var manager = new CharmManagerWindow(_app, initialImagePath: file) { Owner = this };
            manager.ShowDialog();
            RebuildCards();
        }
    }
}
