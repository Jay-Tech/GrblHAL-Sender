using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaApplication1.ViewModels;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Xml.Serialization;

namespace AvaloniaApplication1.Control
{
    public partial class MacroControl : UserControl
    {
        public MacroControl()
        {
            InitializeComponent();
        }
        private void macroToolbarControl_Loaded(object sender, RoutedEventArgs e)
        {
           // Macros = AppConfig.Settings.Macros;
        }

        //public static readonly DependencyProperty MacrosProperty = DependencyProperty.Register(nameof(MacroToolbarControl.Macros), typeof(ObservableCollection<Macro>), typeof(MacroToolbarControl));
        public ObservableCollection<Macro> Macros { get; set; }
        //{
        //    get => (ObservableCollection<Macro>)GetValue(MacrosProperty);
        //    set => SetValue(MacrosProperty, value);
        //}

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var macro = Macros.FirstOrDefault(o =>
            {
                var tag = (sender as Button)?.Tag;
                return tag != null && o.Id == (int)tag;
            });
            //if (macro != null && (!macro.ConfirmOnExecute || MessageBus.Show(string.Format((string)FindResource("RunMacro"), macro.Name), "ioSender",
            //        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))
                (DataContext as MainViewModel)?.ExecuteMacro(macro.Code);
                
        }
    }
    [Serializable]
    public class Macro : ViewModelBase
    {
        string _name;

        [XmlIgnore]
        public bool IsSession { get; set; }

        public int Id { get; set; }
        public string Name { get { return _name; } set { _name = value; OnPropertyChanged(nameof(Name)); } }
        public bool ConfirmOnExecute { get; set; } = true;
        public string Code { get; set; }
        public bool isJob { get; set; }

        public string Path { get; set; }
    }
}
