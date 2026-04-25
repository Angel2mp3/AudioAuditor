// Resolve ambiguities between System.Windows.Forms and WPF types
// (WindowsForms assembly is referenced directly for NotifyIcon tray support)
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using DragEventArgs = System.Windows.DragEventArgs;
global using FontFamily = System.Windows.Media.FontFamily;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
