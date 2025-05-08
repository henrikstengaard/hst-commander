using System.Collections.Specialized;
using System.Text;

namespace Hst.Commander.Console;

using System.Collections;
using Plugins;
using Terminal.Gui;

public class EntryListDataSource(IEnumerable<IEntry> entries) : IListDataSource
{
    private List<IEntry> entries = entries.ToList();

    // 			private readonly int len;
//
// 			public List<Type> Scenarios { get; set; }
//
// 			public bool IsMarked (int item) => false;
//
// 			public int Count => Scenarios.Count;
//
// 			public int Length => len;
//
// 			public EntryListDataSource (List<Type> itemList)
// 			{
// 				Scenarios = itemList;
// 				len = GetMaxLengthItem ();
// 			}
//
// 			public void Render (ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width, int start = 0)
// 			{
// 				container.Move (col, line);
// 				// Equivalent to an interpolated string like $"{Scenarios[item].Name, -widtestname}"; if such a thing were possible
// 				var s = String.Format (String.Format ("{{0,{0}}}", -_nameColumnWidth), Scenario.ScenarioMetadata.GetName (Scenarios [item]));
// 				RenderUstr (driver, $"{s}  {Scenario.ScenarioMetadata.GetDescription (Scenarios [item])}", col, line, width, start);
// 			}
//
// 			public void SetMark (int item, bool value)
// 			{
// 			}
//
// 			int GetMaxLengthItem ()
// 			{
// 				if (Scenarios?.Count == 0) {
// 					return 0;
// 				}
//
// 				int maxLength = 0;
// 				for (int i = 0; i < Scenarios.Count; i++) {
// 					var s = String.Format (String.Format ("{{0,{0}}}", -_nameColumnWidth), Scenario.ScenarioMetadata.GetName (Scenarios [i]));
// 					var sc = $"{s}  {Scenario.ScenarioMetadata.GetDescription (Scenarios [i])}";
// 					var l = sc.Length;
// 					if (l > maxLength) {
// 						maxLength = l;
// 					}
// 				}
//
// 				return maxLength;
// 			}
//
// 			// A slightly adapted method from: https://github.com/gui-cs/Terminal.Gui/blob/fc1faba7452ccbdf49028ac49f0c9f0f42bbae91/Terminal.Gui/Views/ListView.cs#L433-L461
// 			private void RenderUstr (ConsoleDriver driver, ustring ustr, int col, int line, int width, int start = 0)
// 			{
// 				int used = 0;
// 				int index = start;
// 				while (index < ustr.Length) {
// 					(var rune, var size) = Utf8.DecodeRune (ustr, index, index - ustr.Length);
// 					var count = Rune.ColumnWidth (rune);
// 					if (used + count >= width) break;
// 					driver.AddRune (rune);
// 					used += count;
// 					index += size;
// 				}
//
// 				while (used < width) {
// 					driver.AddRune (' ');
// 					used++;
// 				}
// 			}
//

    public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width,
        int start = 0)
    {
        driver.AddStr(entries[item].Name);
    }

    public bool IsMarked(int item)
    {
        return false;
    }

    public void Render(ListView listView, bool selected, int item, int col, int line, int width, int start = 0)
    {
        RenderUstr (listView, entries[item].Name, col, line, width, start);
    }
    
    // A slightly adapted method from: https://github.com/gui-cs/Terminal.Gui/blob/fc1faba7452ccbdf49028ac49f0c9f0f42bbae91/Terminal.Gui/Views/ListView.cs#L433-L461
    private void RenderUstr (View view, string ustr, int col, int line, int width, int start = 0)
    {
        var used = 0;
        int index = start;

        while (index < ustr.Length)
        {
            (Rune rune, int size) = ustr.DecodeRune (index, index - ustr.Length);
            int count = rune.GetColumns ();

            if (used + count >= width)
            {
                break;
            }

            view.AddRune (rune);
            used += count;
            index += size;
        }

        while (used < width)
        {
            view.AddRune ((Rune)' ');
            used++;
        }
    }

    public void SetMark(int item, bool value)
    {
    }

    IList IListDataSource.ToList()
    {
        return entries;
    }

    public int Count => entries.Count;
    public int Length => entries.Count;
    public bool SuspendCollectionChangedEvent { get; set; }
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public void SetEntries(IEnumerable<IEntry> entries)
    {
        this.entries = entries.ToList();
    }

    public void Dispose()
    {
        
    }
}