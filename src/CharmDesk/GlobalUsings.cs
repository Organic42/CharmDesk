// Both WPF and WinForms are referenced in this project (WinForms only for NotifyIcon,
// Screen enumeration, and the registry Run-key helper). Several type names collide between
// the two UI stacks; resolve them project-wide to their WPF equivalents here so individual
// files don't need local aliases.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Point = System.Windows.Point;
global using Cursors = System.Windows.Input.Cursors;
global using Image = System.Windows.Controls.Image;
global using Button = System.Windows.Controls.Button;
global using Orientation = System.Windows.Controls.Orientation;
global using Brush = System.Windows.Media.Brush;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using Color = System.Windows.Media.Color;
global using FontFamily = System.Windows.Media.FontFamily;
global using Brushes = System.Windows.Media.Brushes;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
