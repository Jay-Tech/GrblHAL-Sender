using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GrbLHALSender.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GrbLHALSender.Views
{
    public partial class SurfacingView : UserControl
    {
        public SurfacingView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            if (DataContext is SurfacingViewModel vm)
            {
                vm.SaveFileAsync = SaveFileAsync;
            }
            base.OnDataContextChanged(e);
        }

        private async Task<string?> SaveFileAsync(string defaultName, string content)
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.StorageProvider == null) return null;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save surfacing G-code",
                SuggestedFileName = defaultName,
                DefaultExtension = "nc",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("G-code (*.nc, *.gcode, *.tap)")
                    {
                        Patterns = new[] { "*.nc", "*.gcode", "*.tap" }
                    }
                }
            });

            if (file == null) return null;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);

            return file.Path?.LocalPath ?? file.Name;
        }
    }
}
