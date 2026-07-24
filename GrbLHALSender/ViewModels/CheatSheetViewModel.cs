using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using GrbLHALSender.Data;
using ReactiveUI;

namespace GrbLHALSender.ViewModels;

public class CheatSheetViewModel : ViewModelBase
{
    private string _filterText = string.Empty;
    private string _category = "All";
    private IReadOnlyList<CheatSection> _sections = CheatSheetData.Sections;

    public string FilterText
    {
        get => _filterText;
        set => this.RaiseAndSetIfChanged(ref _filterText, value);
    }

    public string Category
    {
        get => _category;
        set => this.RaiseAndSetIfChanged(ref _category, value);
    }

    public IReadOnlyList<CheatSection> Sections
    {
        get => _sections;
        private set => this.RaiseAndSetIfChanged(ref _sections, value);
    }

    public IReadOnlyList<string> Categories => CheatSheetData.Categories;

    public ICommand CategoryCommand { get; }
    public ICommand ClearFilterCommand { get; }

    public CheatSheetViewModel()
    {
        CategoryCommand = ReactiveCommand.Create<string>(c => Category = c);
        ClearFilterCommand = ReactiveCommand.Create(() => FilterText = string.Empty);

        // Small dataset (~200 rows) — rebuilding synchronously per keystroke
        // is cheap enough even on the Pi.
        this.WhenAnyValue(x => x.FilterText, x => x.Category)
            .Subscribe(_ => Rebuild());
    }

    private void Rebuild()
    {
        var query = FilterText?.Trim() ?? string.Empty;
        Sections = CheatSheetData.Sections
            .Where(s => Category == "All" || s.Category == Category)
            .Select(s => s.Filter(query))
            .Where(s => s is not null)
            .Cast<CheatSection>()
            .ToList();
    }
}
